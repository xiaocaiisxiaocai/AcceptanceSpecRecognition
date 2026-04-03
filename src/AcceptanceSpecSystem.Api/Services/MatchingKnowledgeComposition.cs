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

        var entityAliases = DeserializeStringDictionary(entity.EntityAliasesJson);
        var unitAliases = DeserializeStringDictionary(entity.UnitAliasesJson);
        var unitFactors = DeserializeDecimalDictionary(entity.UnitFactorsJson);
        var fieldAliases = DeserializeStringDictionary(entity.FieldAliasesJson);
        var conflictPairs = DeserializeConflictPairs(entity.ConflictPairsJson);

        return NormalizeLayer(new MatchingKnowledgeLayerDto
        {
            EntityGroups = AggregateAliasGroups(entityAliases),
            UnitGroups = AggregateAliasGroups(unitAliases),
            UnitFactors = unitFactors,
            FieldGroups = AggregateAliasGroups(fieldAliases),
            ConflictGroups = AggregateConflictGroups(conflictPairs)
        });
    }

    public static MatchingKnowledgeLayerDto CreateSeedLayer(MatchingKnowledgeOptions options)
    {
        var unitAliases = NormalizeStringDictionary(options.UnitAliases);
        ExpandMicroSymbolAliases(unitAliases);

        return NormalizeLayer(new MatchingKnowledgeLayerDto
        {
            EntityGroups = AggregateAliasGroups(options.EntityAliases),
            UnitGroups = AggregateAliasGroups(unitAliases),
            UnitFactors = new Dictionary<string, decimal>(options.UnitFactors, StringComparer.OrdinalIgnoreCase),
            FieldGroups = AggregateAliasGroups(options.FieldAliases),
            ConflictGroups = AggregateConflictGroups(options.ConflictPairs.Select(item => new ConflictPairDto
            {
                Left = item.Left,
                Right = item.Right
            }))
        });
    }

    public static MatchingKnowledgeLayerDto NormalizeRequest(UpdateMatchingKnowledgeRequest request)
    {
        return NormalizeLayer(new MatchingKnowledgeLayerDto
        {
            EntityGroups = request.EntityGroups,
            UnitGroups = request.UnitGroups,
            UnitFactors = request.UnitFactors,
            FieldGroups = request.FieldGroups,
            ConflictGroups = request.ConflictGroups
        });
    }

    public static MatchingKnowledgeConfig ToEntity(MatchingKnowledgeLayerDto layer)
    {
        var domain = ToDomainModel(layer);

        return new MatchingKnowledgeConfig
        {
            EntityAliasesJson = JsonSerializer.Serialize(domain.EntityAliases),
            UnitAliasesJson = JsonSerializer.Serialize(domain.UnitAliases),
            UnitFactorsJson = JsonSerializer.Serialize(domain.UnitFactors),
            FieldAliasesJson = JsonSerializer.Serialize(domain.FieldAliases),
            ConflictPairsJson = JsonSerializer.Serialize(domain.ConflictPairs.Select(item => new ConflictPairOption
            {
                Left = item.Left,
                Right = item.Right
            }).ToList()),
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static MatchingKnowledge ToDomainModel(MatchingKnowledgeLayerDto layer)
    {
        var normalized = NormalizeLayer(layer);

        return new MatchingKnowledge
        {
            EntityAliases = ExpandAliasGroups(normalized.EntityGroups, "实体组"),
            UnitAliases = ExpandAliasGroups(normalized.UnitGroups, "单位组"),
            UnitFactors = NormalizeDecimalDictionary(normalized.UnitFactors),
            FieldAliases = ExpandAliasGroups(normalized.FieldGroups, "字段组"),
            ConflictPairs = ExpandConflictGroups(normalized.ConflictGroups)
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
            EntityGroups = NormalizeGroups(layer.EntityGroups),
            UnitGroups = NormalizeGroups(layer.UnitGroups),
            UnitFactors = NormalizeDecimalDictionary(layer.UnitFactors),
            FieldGroups = NormalizeGroups(layer.FieldGroups),
            ConflictGroups = NormalizeConflictGroups(layer.ConflictGroups)
        };
    }

    private static List<MatchingKnowledgeGroupDto> NormalizeGroups(IEnumerable<MatchingKnowledgeGroupDto>? source)
    {
        if (source == null)
        {
            return [];
        }

        var result = new List<MatchingKnowledgeGroupDto>();
        foreach (var group in source)
        {
            var items = NormalizeItems(group?.Items);
            if (items.Count == 0)
            {
                continue;
            }

            result.Add(new MatchingKnowledgeGroupDto
            {
                Items = items
            });
        }

        return result;
    }

    private static List<MatchingKnowledgeConflictGroupDto> NormalizeConflictGroups(IEnumerable<MatchingKnowledgeConflictGroupDto>? source)
    {
        if (source == null)
        {
            return [];
        }

        var result = new List<MatchingKnowledgeConflictGroupDto>();
        foreach (var group in source)
        {
            var leftItems = NormalizeItems(group?.LeftItems);
            var rightItems = NormalizeItems(group?.RightItems);
            if (leftItems.Count == 0 && rightItems.Count == 0)
            {
                continue;
            }

            if (leftItems.Count == 0 || rightItems.Count == 0)
            {
                throw new ArgumentException("冲突组的左右两侧都必须至少包含一个词项");
            }

            result.Add(new MatchingKnowledgeConflictGroupDto
            {
                LeftItems = leftItems,
                RightItems = rightItems
            });
        }

        return result;
    }

    private static List<string> NormalizeItems(IEnumerable<string>? source)
    {
        if (source == null)
        {
            return [];
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in source)
        {
            var normalized = item?.Trim();
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
            {
                continue;
            }

            result.Add(normalized);
        }

        return result;
    }

    private static Dictionary<string, string> ExpandAliasGroups(
        IEnumerable<MatchingKnowledgeGroupDto> groups,
        string categoryName)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            if (group.Items.Count == 0)
            {
                continue;
            }

            var canonical = group.Items[0];
            foreach (var item in group.Items)
            {
                if (result.TryGetValue(item, out var existingCanonical) &&
                    !string.Equals(existingCanonical, canonical, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"{categoryName}词项“{item}”不能归属多个标准值");
                }

                result[item] = canonical;
            }
        }

        return result;
    }

    private static List<(string Left, string Right)> ExpandConflictGroups(IEnumerable<MatchingKnowledgeConflictGroupDto> groups)
    {
        var result = new List<(string Left, string Right)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var overlap = group.LeftItems
                .Intersect(group.RightItems, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(overlap))
            {
                throw new ArgumentException($"冲突组左右两侧不能同时包含词项“{overlap}”");
            }

            foreach (var left in group.LeftItems)
            {
                foreach (var right in group.RightItems)
                {
                    var key = BuildConflictKey(left, right);
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    result.Add((left, right));
                }
            }
        }

        return result;
    }

    private static List<MatchingKnowledgeGroupDto> AggregateAliasGroups(IReadOnlyDictionary<string, string> source)
    {
        var normalized = NormalizeStringDictionary(source);
        var grouped = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in normalized)
        {
            if (!grouped.TryGetValue(pair.Value, out var items))
            {
                items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                grouped[pair.Value] = items;
            }

            items.Add(pair.Key);
        }

        return grouped
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair =>
            {
                var items = new List<string> { pair.Key };
                items.AddRange(pair.Value
                    .Where(item => !string.Equals(item, pair.Key, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase));

                return new MatchingKnowledgeGroupDto
                {
                    Items = items
                };
            })
            .ToList();
    }

    private static List<MatchingKnowledgeConflictGroupDto> AggregateConflictGroups(IEnumerable<ConflictPairDto> source)
    {
        var pairs = NormalizeConflictPairs(source);
        if (pairs.Count == 0)
        {
            return [];
        }

        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairs)
        {
            AddNeighbor(adjacency, pair.Left, pair.Right);
            AddNeighbor(adjacency, pair.Right, pair.Left);
        }

        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<MatchingKnowledgeConflictGroupDto>();

        foreach (var pair in pairs)
        {
            if (processed.Contains(pair.Left) || processed.Contains(pair.Right))
            {
                continue;
            }

            var componentNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var colors = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                [pair.Left] = false,
                [pair.Right] = true
            };
            var queue = new Queue<string>([pair.Left, pair.Right]);
            var valid = true;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                componentNodes.Add(current);

                foreach (var neighbor in adjacency[current])
                {
                    var expectedColor = !colors[current];
                    if (colors.TryGetValue(neighbor, out var actualColor))
                    {
                        if (actualColor != expectedColor)
                        {
                            valid = false;
                        }

                        continue;
                    }

                    colors[neighbor] = expectedColor;
                    queue.Enqueue(neighbor);
                }
            }

            processed.UnionWith(componentNodes);

            if (!valid)
            {
                result.Add(new MatchingKnowledgeConflictGroupDto
                {
                    LeftItems = [pair.Left],
                    RightItems = [pair.Right]
                });
                continue;
            }

            var leftItems = OrderItems(componentNodes.Where(node => !colors[node]), pair.Left);
            var rightItems = OrderItems(componentNodes.Where(node => colors[node]), pair.Right);
            result.Add(new MatchingKnowledgeConflictGroupDto
            {
                LeftItems = leftItems,
                RightItems = rightItems
            });
        }

        return result;
    }

    private static void AddNeighbor(
        IDictionary<string, HashSet<string>> adjacency,
        string key,
        string neighbor)
    {
        if (!adjacency.TryGetValue(key, out var neighbors))
        {
            neighbors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            adjacency[key] = neighbors;
        }

        neighbors.Add(neighbor);
    }

    private static List<string> OrderItems(IEnumerable<string> items, string first)
    {
        var normalizedItems = items
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var index = normalizedItems.FindIndex(item => string.Equals(item, first, StringComparison.OrdinalIgnoreCase));
        if (index > 0)
        {
            normalizedItems.RemoveAt(index);
            normalizedItems.Insert(0, first);
        }

        return normalizedItems;
    }

    private static Dictionary<string, string> NormalizeStringDictionary(IReadOnlyDictionary<string, string> source)
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

    private static Dictionary<string, decimal> NormalizeDecimalDictionary(IReadOnlyDictionary<string, decimal> source)
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
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase) <= 0
            ? $"{left.Trim()}::{right.Trim()}"
            : $"{right.Trim()}::{left.Trim()}";
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
