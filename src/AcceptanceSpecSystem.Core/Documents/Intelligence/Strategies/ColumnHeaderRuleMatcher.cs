using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;

namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;

/// <summary>
/// 统一解释列头规则，供在线识别和离线缺口分析复用。
/// </summary>
public static class ColumnHeaderRuleMatcher
{
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(200);

    public static ColumnHeaderRuleMatch Match(string? header, ColumnHeaderMappingRule rule)
    {
        var text = header?.Trim() ?? string.Empty;
        var pattern = rule.Pattern?.Trim() ?? string.Empty;
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

        var similarity = CalculateLevenshteinSimilarity(
            text.ToLowerInvariant(),
            pattern.ToLowerInvariant());
        return similarity > 0.8
            ? new ColumnHeaderRuleMatch(true, similarity * 0.9)
            : ColumnHeaderRuleMatch.NoMatch;
    }

    private static bool RegexMatches(string text, string pattern)
    {
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

    private static double CalculateLevenshteinSimilarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

        var distance = CalculateLevenshteinDistance(left, right);
        return 1.0 - (double)distance / Math.Max(left.Length, right.Length);
    }

    private static int CalculateLevenshteinDistance(string left, string right)
    {
        var distances = new int[left.Length + 1, right.Length + 1];
        for (var i = 0; i <= left.Length; i++)
        {
            distances[i, 0] = i;
        }

        for (var j = 0; j <= right.Length; j++)
        {
            distances[0, j] = j;
        }

        for (var j = 1; j <= right.Length; j++)
        {
            for (var i = 1; i <= left.Length; i++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost);
            }
        }

        return distances[left.Length, right.Length];
    }
}

public readonly record struct ColumnHeaderRuleMatch(bool Matched, double Confidence)
{
    public static ColumnHeaderRuleMatch NoMatch => new(false, 0);
}
