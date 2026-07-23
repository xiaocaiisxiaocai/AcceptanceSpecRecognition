using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Interfaces;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public sealed partial class SpecCanonicalizer
{
    private static string FullWidthToHalfWidth(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            // 全角字母数字：U+FF01~U+FF5E → 对应半角
            if (ch >= '！' && ch <= '～')
                sb.Append((char)(ch - 0xFEE0));
            // 全角空格
            else if (ch == '　')
                sb.Append(' ');
            else
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private static string ApplySynonymMap(string text)
    {
        // 按关键词长度从长到短替换，避免短词先替换后破坏长词
        foreach (var (from, to) in SynonymMap.OrderByDescending(p => p.Key.Length))
        {
            text = text.Replace(from, to, StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }

    public bool TryNormalizeBrandToken(string brandToken, out string normalizedBrand)
    {
        normalizedBrand = string.Empty;
        if (string.IsNullOrWhiteSpace(brandToken))
            return false;

        if (_brandNormMap.TryGetValue(brandToken.Trim(), out var normalized))
        {
            normalizedBrand = normalized;
            return true;
        }

        return false;
    }

    private string ApplyBrandNorm(string text)
    {
        return _brandNormRegex.Replace(text, match =>
        {
            var token = match.Groups["brand"].Value;
            return _brandNormMap.TryGetValue(token, out var normalized) ? normalized : token;
        });
    }

    private string NormalizeBrandDeviceSpacing(string text)
    {
        return _brandDeviceSpacingRegex.Replace(text, match =>
            $"{match.Groups["brand"].Value}{match.Groups["device"].Value}");
    }

    private static string BuildRegexAlternation(IEnumerable<string> values)
    {
        return string.Join(
            "|",
            values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(value => value.Length)
                .Select(Regex.Escape));
    }

    private static bool IsKnownPlainSuffix(string unit)
    {
        return string.Equals(unit, "%", StringComparison.Ordinal) ||
               string.Equals(unit, "万像素", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(unit, "像素", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(unit, "pcs", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(unit, "pc", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownCompoundDenominator(string unit)
    {
        return string.Equals(unit, "s", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(unit, "sec", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(unit, "min", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(unit, "h", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(unit, "hr", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsUnknownCjkUnitSeparatedByWhitespace(Match match)
    {
        var unit = match.Groups["unit"].Value;
        if (!Regex.IsMatch(unit, @"^[一-鿿]"))
            return false;

        if (CanNormalizeUnitToken(unit))
            return false;

        var value = match.Value;
        return value.Any(char.IsWhiteSpace);
    }

    /// <summary>
    /// 判断无法归一的 token 是否为中文名词（词组）而非量纲单位。
    /// 前提：调用方已先尝试 <see cref="TryNormalizeToBaseUnit"/>，真正的中文单位（米/秒/毫米等）
    /// 已被命中并排除，能进入此方法的纯汉字 token 必然无法归一为已知量纲，基本是名词
    /// （如"边""边吸取""米高""分钟可调"）。
    /// 规则：token 去掉斜杠分隔后全部由汉字构成（不含字母/数字），即判定为名词，
    /// 跳过以避免误报"未识别单位"冲突。
    /// </summary>
    private static bool IsCjkNounPhraseUnit(string unit)
    {
        if (string.IsNullOrEmpty(unit))
            return false;

        // 含字母或数字的 token（如 m/min、10mm、rpm）不是纯中文名词，
        // 交由常规未识别单位逻辑处理
        if (unit.Any(ch => (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || char.IsDigit(ch)))
            return false;

        // 去掉斜杠（复合单位分隔符）后统计汉字
        var coreChars = unit.Where(ch => ch != '/').ToArray();
        if (coreChars.Length == 0)
            return false;

        // 全部为 CJK 汉字才判定为名词（合法中文单位已在上游 TryNormalizeToBaseUnit 命中排除）
        return coreChars.All(ch => ch >= '一' && ch <= '鿿');
    }

    private bool CanNormalizeUnitToken(string unit)
    {
        if (_unitRoots.ContainsKey(unit))
            return true;

        foreach (var (prefix, _) in SiPrefixFactors.OrderByDescending(p => p.Key.Length))
        {
            if (!unit.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            if (_unitRoots.ContainsKey(unit[prefix.Length..]))
                return true;
        }

        return false;
    }

}
