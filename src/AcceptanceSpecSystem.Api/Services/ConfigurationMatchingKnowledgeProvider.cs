using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 从受控配置中提供匹配知识
/// </summary>
public sealed class ConfigurationMatchingKnowledgeProvider : IMatchingKnowledgeProvider
{
    private readonly IMatchingKnowledgeConfigRepository _repository;
    private readonly MatchingKnowledgeOptions _defaultOptions;

    public ConfigurationMatchingKnowledgeProvider(
        IMatchingKnowledgeConfigRepository repository,
        IOptions<MatchingKnowledgeOptions> options)
    {
        _repository = repository;
        _defaultOptions = options.Value ?? new MatchingKnowledgeOptions();
    }

    public async Task<MatchingKnowledge> GetKnowledgeAsync(CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetConfigAsync();
        if (entity == null)
        {
            return CreateFromOptions(_defaultOptions);
        }

        return new MatchingKnowledge
        {
            EntityAliases = DeserializeDictionary(entity.EntityAliasesJson),
            UnitAliases = DeserializeDictionary(entity.UnitAliasesJson),
            UnitFactors = DeserializeDecimalDictionary(entity.UnitFactorsJson),
            FieldAliases = DeserializeDictionary(entity.FieldAliasesJson),
            ConflictPairs = DeserializeConflictPairs(entity.ConflictPairsJson)
        };
    }

    private static MatchingKnowledge CreateFromOptions(MatchingKnowledgeOptions options)
    {
        return new MatchingKnowledge
        {
            EntityAliases = new Dictionary<string, string>(options.EntityAliases, StringComparer.OrdinalIgnoreCase),
            UnitAliases = new Dictionary<string, string>(options.UnitAliases, StringComparer.OrdinalIgnoreCase),
            UnitFactors = new Dictionary<string, decimal>(options.UnitFactors, StringComparer.OrdinalIgnoreCase),
            FieldAliases = new Dictionary<string, string>(options.FieldAliases, StringComparer.OrdinalIgnoreCase),
            ConflictPairs = options.ConflictPairs
                .Where(item => !string.IsNullOrWhiteSpace(item.Left) && !string.IsNullOrWhiteSpace(item.Right))
                .Select(item => (item.Left.Trim(), item.Right.Trim()))
                .ToList()
        };
    }

    private static Dictionary<string, string> DeserializeDictionary(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        return new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, decimal> DeserializeDecimalDictionary(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, decimal>>(json) ?? [];
        return new Dictionary<string, decimal>(raw, StringComparer.OrdinalIgnoreCase);
    }

    private static List<(string Left, string Right)> DeserializeConflictPairs(string json)
    {
        var pairs = JsonSerializer.Deserialize<List<ConflictPairOption>>(json) ?? [];
        return pairs
            .Where(item => !string.IsNullOrWhiteSpace(item.Left) && !string.IsNullOrWhiteSpace(item.Right))
            .Select(item => (item.Left.Trim(), item.Right.Trim()))
            .ToList();
    }
}
