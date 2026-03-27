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
            if (text.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
                return (pair.Key, pair.Value);
        }

        return null;
    }
}
