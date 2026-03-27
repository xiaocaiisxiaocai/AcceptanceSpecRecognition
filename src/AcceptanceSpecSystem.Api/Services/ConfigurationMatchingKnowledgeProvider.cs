using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 从受控配置中提供匹配知识
/// </summary>
public sealed class ConfigurationMatchingKnowledgeProvider : IMatchingKnowledgeProvider
{
    private readonly MatchingKnowledgeOptions _options;

    public ConfigurationMatchingKnowledgeProvider(IOptions<MatchingKnowledgeOptions> options)
    {
        _options = options.Value ?? new MatchingKnowledgeOptions();
    }

    public Task<MatchingKnowledge> GetKnowledgeAsync(CancellationToken cancellationToken = default)
    {
        var knowledge = new MatchingKnowledge
        {
            EntityAliases = new Dictionary<string, string>(_options.EntityAliases, StringComparer.OrdinalIgnoreCase),
            UnitAliases = new Dictionary<string, string>(_options.UnitAliases, StringComparer.OrdinalIgnoreCase),
            UnitFactors = new Dictionary<string, decimal>(_options.UnitFactors, StringComparer.OrdinalIgnoreCase),
            FieldAliases = new Dictionary<string, string>(_options.FieldAliases, StringComparer.OrdinalIgnoreCase),
            ConflictPairs = _options.ConflictPairs
                .Where(item => !string.IsNullOrWhiteSpace(item.Left) && !string.IsNullOrWhiteSpace(item.Right))
                .Select(item => (item.Left.Trim(), item.Right.Trim()))
                .ToList()
        };

        return Task.FromResult(knowledge);
    }
}
