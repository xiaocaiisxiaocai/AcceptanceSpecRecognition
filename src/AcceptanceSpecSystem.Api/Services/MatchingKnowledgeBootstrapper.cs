using System.Text.Json;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 匹配知识初始化器。
/// </summary>
public sealed class MatchingKnowledgeBootstrapper
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly MatchingKnowledgeOptions _defaultOptions;

    /// <summary>
    /// 初始化匹配知识初始化器。
    /// </summary>
    public MatchingKnowledgeBootstrapper(
        IUnitOfWork unitOfWork,
        IOptions<MatchingKnowledgeOptions> defaultOptions)
    {
        _unitOfWork = unitOfWork;
        _defaultOptions = defaultOptions.Value ?? new MatchingKnowledgeOptions();
    }

    /// <summary>
    /// 确保数据库内存在当前生效的匹配知识配置。
    /// </summary>
    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _unitOfWork.MatchingKnowledgeConfigs.GetConfigAsync();
        if (existing != null)
        {
            return;
        }

        var seed = CreateSeedFromDefaults(_defaultOptions);

        await _unitOfWork.MatchingKnowledgeConfigs.SaveConfigAsync(new MatchingKnowledgeConfig
        {
            EntityAliasesJson = JsonSerializer.Serialize(seed.EntityAliases),
            UnitAliasesJson = JsonSerializer.Serialize(seed.UnitAliases),
            UnitFactorsJson = JsonSerializer.Serialize(seed.UnitFactors),
            FieldAliasesJson = JsonSerializer.Serialize(seed.FieldAliases),
            ConflictPairsJson = JsonSerializer.Serialize(seed.ConflictPairs),
            UpdatedAt = DateTime.UtcNow
        });

        await _unitOfWork.SaveChangesAsync();
    }

    private static SeedPayload CreateSeedFromDefaults(MatchingKnowledgeOptions options)
    {
        var seed = new SeedPayload();

        foreach (var pair in options.EntityAliases)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                seed.EntityAliases[pair.Key.Trim()] = pair.Value.Trim();
            }
        }

        foreach (var pair in options.UnitAliases)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                seed.UnitAliases[pair.Key.Trim()] = pair.Value.Trim();
            }
        }

        foreach (var pair in options.UnitFactors)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                seed.UnitFactors[pair.Key.Trim()] = pair.Value;
            }
        }

        foreach (var pair in options.FieldAliases)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                seed.FieldAliases[pair.Key.Trim()] = pair.Value.Trim();
            }
        }

        foreach (var pair in options.ConflictPairs)
        {
            if (!string.IsNullOrWhiteSpace(pair.Left) && !string.IsNullOrWhiteSpace(pair.Right))
            {
                seed.ConflictPairs.Add(new ConflictPairOption
                {
                    Left = pair.Left.Trim(),
                    Right = pair.Right.Trim()
                });
            }
        }

        return seed;
    }

    private sealed class SeedPayload
    {
        public Dictionary<string, string> EntityAliases { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> UnitAliases { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, decimal> UnitFactors { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> FieldAliases { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<ConflictPairOption> ConflictPairs { get; } = [];
    }
}
