using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Services;

internal sealed class EntitySurfaceExtractor
{
    private static readonly Regex LabeledEntityRegex = new(
        @"(?:品牌|厂商|供应商|vendor|brand|maker)\s*[:：]?\s*(?<entity>[A-Za-z][A-Za-z0-9&.\-]{1,31}|[\u4e00-\u9fff]{2,16})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EnglishEntityRegex = new(
        @"\b[A-Za-z][A-Za-z0-9&.\-]{1,31}\b",
        RegexOptions.Compiled);

    private static readonly Regex ChineseEntityBeforeGenericSuffixRegex = new(
        @"(?<entity>[\u4e00-\u9fff]{2,16}?)(?:设备|品牌|厂商|供应商|产品|系统)",
        RegexOptions.Compiled);

    private static readonly Regex ChineseCompanyRegex = new(
        @"(?<entity>[\u4e00-\u9fff]{2,20}?)(?:股份有限公司|有限公司|集团|公司)",
        RegexOptions.Compiled);

    private static readonly HashSet<string> EnglishStopWords =
    [
        "device", "devices", "equipment", "vendor", "brand", "maker", "supplier",
        "install", "installation", "need", "requires", "requirement", "power", "system"
    ];

    private static readonly string[] NormalizationSuffixes =
    [
        "股份有限公司", "有限公司", "company limited", "co., ltd.", "co.,ltd.", "co., ltd",
        "co ltd", "co.ltd", "limited", "corporation", "corp.", "corp", "inc.", "inc",
        "company", "集团", "公司", "品牌", "厂商", "供应商", "设备", "产品", "系统"
    ];

    public EntitySurfaceCandidate? Extract(string? text, MatchingKnowledge knowledge)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var pair in knowledge.EntityAliases)
        {
            if (ContainsAlias(text, pair.Key))
            {
                return new EntitySurfaceCandidate(pair.Key, pair.Value);
            }
        }

        var labeled = BuildCandidate(TryGetNamedMatch(LabeledEntityRegex, text, "entity"));
        if (labeled != null)
        {
            return labeled;
        }

        foreach (Match match in EnglishEntityRegex.Matches(text))
        {
            var token = match.Value.Trim();
            if (EnglishStopWords.Contains(token.ToLowerInvariant()))
            {
                continue;
            }

            var candidate = BuildCandidate(token);
            if (candidate != null)
            {
                return candidate;
            }
        }

        var chineseWithSuffix = BuildCandidate(TryGetNamedMatch(ChineseEntityBeforeGenericSuffixRegex, text, "entity"));
        if (chineseWithSuffix != null)
        {
            return chineseWithSuffix;
        }

        var chineseCompany = BuildCandidate(TryGetNamedMatch(ChineseCompanyRegex, text, "entity"));
        if (chineseCompany != null)
        {
            return chineseCompany;
        }

        return null;
    }

    private static string? TryGetNamedMatch(Regex regex, string text, string groupName)
    {
        var match = regex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups[groupName].Value;
    }

    private static EntitySurfaceCandidate? BuildCandidate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim().Trim('：', ':', '-', '(', ')', '（', '）', '[', ']', '【', '】', '，', ',', '。', '.');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        var normalized = trimmed;
        foreach (var suffix in NormalizationSuffixes)
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[..^suffix.Length].Trim();
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 2)
        {
            return null;
        }

        return new EntitySurfaceCandidate(trimmed, normalized);
    }

    private static bool ContainsAlias(string text, string alias)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(alias))
        {
            return false;
        }

        if (!alias.Any(ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'))
        {
            return text.Contains(alias, StringComparison.OrdinalIgnoreCase);
        }

        var startIndex = 0;
        while (startIndex < text.Length)
        {
            var index = text.IndexOf(alias, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var beforeIsBoundary = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var afterIndex = index + alias.Length;
            var afterIsBoundary = afterIndex >= text.Length || !char.IsLetterOrDigit(text[afterIndex]);
            if (beforeIsBoundary && afterIsBoundary)
            {
                return true;
            }

            startIndex = index + alias.Length;
        }

        return false;
    }
}

internal sealed record EntitySurfaceCandidate(string Raw, string Normalized);
