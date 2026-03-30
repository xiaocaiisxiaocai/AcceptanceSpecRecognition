using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Services;

internal sealed class EntityAliasNormalizer
{
    public (string Raw, string Normalized)? Extract(string text, MatchingKnowledge knowledge)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        foreach (var pair in knowledge.EntityAliases)
        {
            if (ContainsAlias(text, pair.Key))
                return (pair.Key, pair.Value);
        }

        return null;
    }

    private static bool ContainsAlias(string text, string alias)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(alias))
            return false;

        if (!alias.Any(ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'))
            return text.Contains(alias, StringComparison.OrdinalIgnoreCase);

        var startIndex = 0;
        while (startIndex < text.Length)
        {
            var index = text.IndexOf(alias, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            var beforeIsBoundary = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var afterIndex = index + alias.Length;
            var afterIsBoundary = afterIndex >= text.Length || !char.IsLetterOrDigit(text[afterIndex]);
            if (beforeIsBoundary && afterIsBoundary)
                return true;

            startIndex = index + alias.Length;
        }

        return false;
    }
}
