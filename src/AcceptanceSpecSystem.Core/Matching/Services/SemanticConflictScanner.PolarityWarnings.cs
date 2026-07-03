using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public sealed partial class SemanticConflictScanner
{
    private static List<(bool IsNegative, string Subject, string Text)> ExtractPolarityStatements(string text)
    {
        var normalized = Regex.Replace(text ?? string.Empty, @"\s+", string.Empty);
        var result = new List<(bool, string, string)>();

        foreach (var prefix in NegativePrefixTerms.OrderByDescending(item => item.Length))
        {
            foreach (Match match in Regex.Matches(
                         normalized,
                         $"{Regex.Escape(prefix)}(?<subject>[A-Za-z0-9\\u4e00-\\u9fff]{{1,20}})",
                         RegexOptions.IgnoreCase))
            {
                AddPolarityStatement(result, isNegative: true, prefix, match.Groups["subject"].Value);
            }
        }

        foreach (var prefix in PositivePrefixTerms.OrderByDescending(item => item.Length))
        {
            foreach (Match match in Regex.Matches(
                         normalized,
                         $"{Regex.Escape(prefix)}(?<subject>[A-Za-z0-9\\u4e00-\\u9fff]{{1,20}})",
                         RegexOptions.IgnoreCase))
            {
                var subject = match.Groups["subject"].Value;
                if (NegativePrefixTerms.Any(negative => subject.StartsWith(negative, StringComparison.OrdinalIgnoreCase)))
                    continue;

                AddPolarityStatement(result, isNegative: false, prefix, subject);
            }
        }

        return result;
    }

    private static void AddPolarityStatement(
        List<(bool IsNegative, string Subject, string Text)> result,
        bool isNegative,
        string prefix,
        string rawSubject)
    {
        var subject = NormalizePolaritySubject(rawSubject);
        if (string.IsNullOrWhiteSpace(subject))
            return;

        result.Add((isNegative, subject, $"{prefix}{subject}"));
    }

    private static string NormalizePolaritySubject(string value)
    {
        var subject = Regex.Replace(value ?? string.Empty, @"[，,。；;：:\(\)（）\[\]【】].*$", string.Empty).Trim();
        subject = Regex.Replace(subject, @"(?:功能|要求|项目|项|内容)$", string.Empty).Trim();
        return subject;
    }

    private void ScanUnknownUnitWarnings(MatchEvidence evidence, string srcText, string candText)
    {
        var srcUnknownUnits = _canonicalizer.ExtractUnknownUnitTokens(srcText);
        var candUnknownUnits = _canonicalizer.ExtractUnknownUnitTokens(candText);
        if (srcUnknownUnits.Count == 0 && candUnknownUnits.Count == 0)
            return;

        var srcTokens = srcUnknownUnits
            .Select(item => item.UnitToken)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var candTokens = candUnknownUnits
            .Select(item => item.UnitToken)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (srcTokens.SequenceEqual(candTokens, StringComparer.OrdinalIgnoreCase))
            return;

        var srcValue = FormatUnknownTokenValue(srcUnknownUnits.Select(item => item.OriginalExpression));
        var candValue = FormatUnknownTokenValue(candUnknownUnits.Select(item => item.OriginalExpression));
        var msg = $"存在未识别单位，禁止确定性自动通过：{srcValue} vs {candValue}";
        evidence.Warnings.Add(msg);
        evidence.Issues.Add(new MatchIssue
        {
            Code = "unknown_unit_token",
            Severity = "warning",
            FieldName = "单位",
            SourceValue = srcValue,
            CandidateValue = candValue,
            Message = msg,
            SuggestedAction = "请交由 LLM 或人工确认未识别单位是否等价"
        });
    }

    private void ScanUnknownBrandWarnings(MatchEvidence evidence, string srcText, string candText)
    {
        var srcBrands = ExtractContextBrands(srcText);
        var candBrands = ExtractContextBrands(candText);
        if (srcBrands.Count == 0 || candBrands.Count == 0)
            return;

        var srcDistinct = srcBrands
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var candDistinct = candBrands
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var srcCanonical = srcDistinct
            .Select(CanonicalizeBrandForComparison)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var candCanonical = candDistinct
            .Select(CanonicalizeBrandForComparison)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (srcCanonical.SequenceEqual(candCanonical, StringComparer.OrdinalIgnoreCase))
            return;

        var srcValue = string.Join("、", srcDistinct);
        var candValue = string.Join("、", candDistinct);
        var msg = $"存在品牌差异或未识别品牌，禁止确定性自动通过：{srcValue} vs {candValue}";
        evidence.Warnings.Add(msg);
        evidence.Issues.Add(new MatchIssue
        {
            Code = "unknown_brand_token",
            Severity = "warning",
            FieldName = "品牌",
            SourceValue = srcValue,
            CandidateValue = candValue,
            Message = msg,
            SuggestedAction = "请交由 LLM 或人工确认品牌是否为同一实体"
        });
    }

    private static void ScanUnsupportedFormatWarnings(MatchEvidence evidence, string srcText, string candText)
    {
        var srcTokens = ExtractUnsupportedFormatTokens(srcText);
        var candTokens = ExtractUnsupportedFormatTokens(candText);
        if (srcTokens.Count == 0 && candTokens.Count == 0)
            return;

        // 两侧同类未覆盖格式完全一致时不额外拦截；差异表达需人工确认，避免 LLM 误放行。
        if (srcTokens.SequenceEqual(candTokens, StringComparer.OrdinalIgnoreCase))
            return;

        var srcValue = FormatUnknownTokenValue(srcTokens);
        var candValue = FormatUnknownTokenValue(candTokens);
        var msg = $"存在规则未覆盖的自然语言/中文数字格式，禁止自动通过：{srcValue} vs {candValue}";
        evidence.Warnings.Add(msg);
        evidence.Issues.Add(new MatchIssue
        {
            Code = "unsupported_format_token",
            Severity = "warning",
            FieldName = "格式",
            SourceValue = srcValue,
            CandidateValue = candValue,
            Message = msg,
            SuggestedAction = "请人工确认自然语言数字或格式表达是否等价"
        });
    }

    private static List<string> ExtractUnsupportedFormatTokens(string text)
    {
        var result = new List<string>();
        result.AddRange(UnsupportedChineseNumberRegex.Matches(text).Select(match => match.Value));
        result.AddRange(UnsupportedNaturalFormatRegex.Matches(text).Select(match => match.Value));
        return result
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatUnknownTokenValue(IEnumerable<string> values)
    {
        var distinct = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return distinct.Count == 0 ? "无" : string.Join("、", distinct);
    }

    private static List<string> ExtractContextBrands(string text)
    {
        return BrandContextRegex.Matches(text)
            .Select(match => NormalizeBrandToken(match.Groups["brand"].Value))
            .Where(IsValidBrandToken)
            .ToList();
    }

    private static bool IsValidBrandToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var trimmed = token.Trim();
        if (NonBrandContextTokens.Contains(trimmed))
            return false;

        // 项目编号（如 B017/S030）常紧跟“品牌要求”出现在项目列里，不是品牌实体。
        if (Regex.IsMatch(trimmed, @"^[A-Za-z]\d{2,4}$"))
            return false;

        return Regex.IsMatch(trimmed, @"[A-Za-z]") ? trimmed.Length >= 3 : trimmed.Length >= 2;
    }

    private static string NormalizeBrandToken(string value)
    {
        var token = Regex.Replace(value.Trim(), @"\s+", " ");
        token = Regex.Replace(token, @"(?:\s*(?:分辨率|型号|规格|电压|功率|扭矩|转速|，|,|。|;|；).*)$", string.Empty);
        return token.Trim();
    }

    private string CanonicalizeBrandForComparison(string value)
    {
        if (_canonicalizer.TryNormalizeBrandToken(value, out var normalizedBrand))
            return normalizedBrand;

        var canonical = _canonicalizer.Canonicalize(value);
        canonical = Regex.Replace(canonical, @"\s+", " ").Trim();

        foreach (var suffix in BrandDeviceSuffixWords.OrderByDescending(item => item.Length))
        {
            if (!canonical.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            canonical = canonical[..^suffix.Length].Trim();
            break;
        }

        return canonical;
    }
}
