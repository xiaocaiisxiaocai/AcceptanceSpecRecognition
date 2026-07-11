using System.Text.RegularExpressions;
using System.Text;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;

namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;

/// <summary>
/// 统一解释列头规则，供在线识别和离线缺口分析复用。
/// </summary>
public static class ColumnHeaderRuleMatcher
{
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(200);
    public const int MaxHeaderInputLength = 1024;
    private const int MaxFuzzyTextLength = 128;
    private const int MaxRegexInputLength = 1024;
    private const int MaxRegexPatternLength = 256;
    private const int MaxRulePatternInputLength = 256;

    public static ColumnHeaderRuleMatch Match(string? header, ColumnHeaderMappingRule rule)
    {
        return TryNormalizeHeader(header, out var normalizedHeader)
            ? MatchNormalizedHeader(normalizedHeader, rule)
            : ColumnHeaderRuleMatch.NoMatch;
    }

    public static ColumnHeaderRuleMatch MatchNormalizedHeader(
        string normalizedHeader,
        ColumnHeaderMappingRule rule)
    {
        if (normalizedHeader.Length == 0 || normalizedHeader.Length > MaxHeaderInputLength)
        {
            return ColumnHeaderRuleMatch.NoMatch;
        }

        var rawPattern = rule.Pattern;
        if (string.IsNullOrEmpty(rawPattern) || rawPattern.Length > MaxRulePatternInputLength)
        {
            return ColumnHeaderRuleMatch.NoMatch;
        }

        var text = normalizedHeader;
        var pattern = rule.MatchMode == ColumnHeaderMatchMode.Regex
            ? rawPattern.Trim()
            : NormalizeWhitespaceCore(rawPattern);
        if (text.Length == 0 || pattern.Length == 0)
        {
            return ColumnHeaderRuleMatch.NoMatch;
        }

        return rule.MatchMode switch
        {
            ColumnHeaderMatchMode.Equals => string.Equals(
                text,
                pattern,
                StringComparison.OrdinalIgnoreCase)
                ? new ColumnHeaderRuleMatch(true, 0.99)
                : ColumnHeaderRuleMatch.NoMatch,
            ColumnHeaderMatchMode.Regex => RegexMatches(text, pattern)
                ? new ColumnHeaderRuleMatch(true, 0.97)
                : ColumnHeaderRuleMatch.NoMatch,
            _ => MatchContains(text, pattern)
        };
    }

    public static bool IsMatch(string? header, ColumnHeaderMappingRule rule) => Match(header, rule).Matched;

    public static bool TryNormalizeHeader(string? header, out string normalizedHeader)
    {
        if (header == null || header.Length == 0 || header.Length > MaxHeaderInputLength)
        {
            normalizedHeader = string.Empty;
            return false;
        }

        normalizedHeader = NormalizeWhitespaceCore(header);
        return normalizedHeader.Length > 0;
    }

    private static ColumnHeaderRuleMatch MatchContains(string text, string pattern)
    {
        if (string.Equals(text, pattern, StringComparison.OrdinalIgnoreCase))
        {
            return new ColumnHeaderRuleMatch(true, 0.99);
        }

        if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
        {
            var coverage = Math.Clamp((double)pattern.Length / Math.Max(text.Length, 1), 0, 1);
            return new ColumnHeaderRuleMatch(true, Math.Round(0.90 + coverage * 0.08, 3));
        }

        if (text.Length > MaxFuzzyTextLength || pattern.Length > MaxFuzzyTextLength)
        {
            return ColumnHeaderRuleMatch.NoMatch;
        }

        var maxLength = Math.Max(text.Length, pattern.Length);
        var maxDistance = (maxLength - 1) / 5;
        if (Math.Abs(text.Length - pattern.Length) > maxDistance)
        {
            return ColumnHeaderRuleMatch.NoMatch;
        }

        var distance = CalculateLevenshteinDistance(text, pattern, maxDistance);
        if (distance > maxDistance)
        {
            return ColumnHeaderRuleMatch.NoMatch;
        }

        var similarity = 1.0 - (double)distance / maxLength;
        return similarity > 0.8
            ? new ColumnHeaderRuleMatch(true, similarity * 0.9)
            : ColumnHeaderRuleMatch.NoMatch;
    }

    private static bool RegexMatches(string text, string pattern)
    {
        if (text.Length > MaxRegexInputLength || pattern.Length > MaxRegexPatternLength)
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(
                text,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexMatchTimeout);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static int CalculateLevenshteinDistance(
        string left,
        string right,
        int maxDistance)
    {
        if (left.Length > right.Length)
        {
            (left, right) = (right, left);
        }

        var previous = new int[left.Length + 1];
        var current = new int[left.Length + 1];
        for (var i = 0; i <= left.Length; i++)
        {
            previous[i] = i;
        }

        for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
        {
            current[0] = rightIndex;
            var rowMinimum = current[0];

            for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
            {
                var cost = char.ToUpperInvariant(left[leftIndex - 1]) ==
                           char.ToUpperInvariant(right[rightIndex - 1])
                    ? 0
                    : 1;
                current[leftIndex] = Math.Min(
                    Math.Min(previous[leftIndex] + 1, current[leftIndex - 1] + 1),
                    previous[leftIndex - 1] + cost);
                rowMinimum = Math.Min(rowMinimum, current[leftIndex]);
            }

            if (rowMinimum > maxDistance)
            {
                return maxDistance + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[left.Length];
    }

    private static string NormalizeWhitespaceCore(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (character is '\u200B' or '\uFEFF')
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}

public readonly record struct ColumnHeaderRuleMatch(bool Matched, double Confidence)
{
    public static ColumnHeaderRuleMatch NoMatch => new(false, 0);
}
