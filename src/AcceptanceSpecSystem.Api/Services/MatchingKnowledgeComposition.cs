using System.Text.Json;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 匹配知识配置转换与标准化辅助。
/// </summary>
internal static class MatchingKnowledgeComposition
{
    public static MatchingKnowledgeLayerDto ToDto(MatchingKnowledgeConfig? entity)
    {
        if (entity == null)
        {
            return new MatchingKnowledgeLayerDto();
        }

        return NormalizeLayer(new MatchingKnowledgeLayerDto
        {
            EntityAliases = DeserializeStringDictionary(entity.EntityAliasesJson),
            UnitAliases = DeserializeStringDictionary(entity.UnitAliasesJson),
            UnitFactors = DeserializeDecimalDictionary(entity.UnitFactorsJson),
            FieldAliases = DeserializeStringDictionary(entity.FieldAliasesJson),
            ConflictPairs = DeserializeConflictPairs(entity.ConflictPairsJson)
        });
    }

    public static MatchingKnowledgeLayerDto CreateSeedLayer(MatchingKnowledgeOptions options)
    {
        var unitAliases = NormalizeStringDictionary(options.UnitAliases);
        ExpandMicroSymbolAliases(unitAliases);

        return NormalizeLayer(new MatchingKnowledgeLayerDto
        {
            EntityAliases = new Dictionary<string, string>(options.EntityAliases, StringComparer.OrdinalIgnoreCase),
            UnitAliases = unitAliases,
            UnitFactors = new Dictionary<string, decimal>(options.UnitFactors, StringComparer.OrdinalIgnoreCase),
            FieldAliases = new Dictionary<string, string>(options.FieldAliases, StringComparer.OrdinalIgnoreCase),
            ConflictPairs = options.ConflictPairs.Select(item => new ConflictPairDto
            {
                Left = item.Left,
                Right = item.Right
            }).ToList()
        });
    }

    public static MatchingKnowledgeLayerDto NormalizeRequest(UpdateMatchingKnowledgeRequest request)
    {
        return NormalizeLayer(new MatchingKnowledgeLayerDto
        {
            EntityAliases = request.EntityAliases,
            UnitAliases = request.UnitAliases,
            UnitFactors = request.UnitFactors,
            FieldAliases = request.FieldAliases,
            ConflictPairs = request.ConflictPairs
        });
    }

    public static MatchingKnowledgeConfig ToEntity(MatchingKnowledgeLayerDto layer)
    {
        var normalized = NormalizeLayer(layer);

        return new MatchingKnowledgeConfig
        {
            EntityAliasesJson = JsonSerializer.Serialize(normalized.EntityAliases),
            UnitAliasesJson = JsonSerializer.Serialize(normalized.UnitAliases),
            UnitFactorsJson = JsonSerializer.Serialize(normalized.UnitFactors),
            FieldAliasesJson = JsonSerializer.Serialize(normalized.FieldAliases),
            ConflictPairsJson = JsonSerializer.Serialize(normalized.ConflictPairs.Select(item => item.ToOption()).ToList()),
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static MatchingKnowledge ToDomainModel(MatchingKnowledgeLayerDto layer)
    {
        return new MatchingKnowledge
        {
            EntityAliases = new Dictionary<string, string>(layer.EntityAliases, StringComparer.OrdinalIgnoreCase),
            UnitAliases = new Dictionary<string, string>(layer.UnitAliases, StringComparer.OrdinalIgnoreCase),
            UnitFactors = new Dictionary<string, decimal>(layer.UnitFactors, StringComparer.OrdinalIgnoreCase),
            FieldAliases = new Dictionary<string, string>(layer.FieldAliases, StringComparer.OrdinalIgnoreCase),
            ConflictPairs = layer.ConflictPairs
                .Select(item => (item.Left.Trim(), item.Right.Trim()))
                .ToList()
        };
    }

    public static MatchingKnowledgeConfig CreateEmptyEntity()
    {
        return ToEntity(new MatchingKnowledgeLayerDto());
    }

    public static MatchingKnowledgeConfig CreateSeedEntity(MatchingKnowledgeOptions options)
    {
        return ToEntity(CreateSeedLayer(options));
    }

    private static MatchingKnowledgeLayerDto NormalizeLayer(MatchingKnowledgeLayerDto layer)
    {
        return new MatchingKnowledgeLayerDto
        {
            EntityAliases = NormalizeStringDictionary(layer.EntityAliases),
            UnitAliases = NormalizeStringDictionary(layer.UnitAliases),
            UnitFactors = NormalizeDecimalDictionary(layer.UnitFactors),
            FieldAliases = NormalizeStringDictionary(layer.FieldAliases),
            ConflictPairs = NormalizeConflictPairs(layer.ConflictPairs)
        };
    }

    private static Dictionary<string, string> NormalizeStringDictionary(Dictionary<string, string> source)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            var key = pair.Key?.Trim();
            var value = pair.Value?.Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            result[key] = value;
        }

        return result;
    }

    private static void ExpandMicroSymbolAliases(Dictionary<string, string> source)
    {
        var additions = new List<KeyValuePair<string, string>>();
        foreach (var pair in source)
        {
            if (pair.Key.Contains('μ'))
            {
                var alternateKey = pair.Key.Replace('μ', 'µ');
                if (!source.ContainsKey(alternateKey))
                {
                    additions.Add(new KeyValuePair<string, string>(alternateKey, pair.Value));
                }
            }
            else if (pair.Key.Contains('µ'))
            {
                var alternateKey = pair.Key.Replace('µ', 'μ');
                if (!source.ContainsKey(alternateKey))
                {
                    additions.Add(new KeyValuePair<string, string>(alternateKey, pair.Value));
                }
            }
        }

        foreach (var pair in additions)
        {
            source[pair.Key] = pair.Value;
        }
    }

    private static Dictionary<string, decimal> NormalizeDecimalDictionary(Dictionary<string, decimal> source)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            var key = pair.Key?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            result[key] = pair.Value;
        }

        return result;
    }

    private static List<ConflictPairDto> NormalizeConflictPairs(IEnumerable<ConflictPairDto> source)
    {
        var result = new List<ConflictPairDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in source)
        {
            var left = pair.Left?.Trim();
            var right = pair.Right?.Trim();
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                continue;
            }

            var key = BuildConflictKey(left, right);
            if (!seen.Add(key))
            {
                continue;
            }

            result.Add(new ConflictPairDto
            {
                Left = left,
                Right = right
            });
        }

        return result;
    }

    private static string BuildConflictKey(string left, string right)
    {
        return $"{left.Trim()}::{right.Trim()}";
    }

    private static Dictionary<string, string> DeserializeStringDictionary(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        return new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, decimal> DeserializeDecimalDictionary(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, decimal>>(json) ?? [];
        return new Dictionary<string, decimal>(raw, StringComparer.OrdinalIgnoreCase);
    }

    private static List<ConflictPairDto> DeserializeConflictPairs(string json)
    {
        return JsonSerializer.Deserialize<List<ConflictPairDto>>(json) ?? [];
    }
}
