using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using CoreAiServicePurpose = AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 统一加载智能填充候选规格，集中处理数据范围、候选上限、去重与 Embedding 缓存补齐。
/// </summary>
public sealed class MatchingCandidateProvider
{
    private const int MaxScopedCandidateCount = 5000;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAiServiceSelector _aiServiceSelector;
    private readonly IEmbeddingService _embeddingService;
    private readonly SpecEmbeddingCacheService _specEmbeddingCacheService;
    private readonly ILogger<MatchingCandidateProvider> _logger;

    private sealed class CandidateSpecRow
    {
        public int Id { get; init; }
        public string Project { get; init; } = string.Empty;
        public string Specification { get; init; } = string.Empty;
        public string? Acceptance { get; init; }
        public string? Remark { get; init; }
        public DateTime ImportedAt { get; init; }
    }

    public MatchingCandidateProvider(
        IUnitOfWork unitOfWork,
        IAiServiceSelector aiServiceSelector,
        IEmbeddingService embeddingService,
        SpecEmbeddingCacheService specEmbeddingCacheService,
        ILogger<MatchingCandidateProvider> logger)
    {
        _unitOfWork = unitOfWork;
        _aiServiceSelector = aiServiceSelector;
        _embeddingService = embeddingService;
        _specEmbeddingCacheService = specEmbeddingCacheService;
        _logger = logger;
    }

    public async Task<List<MatchCandidate>> GetCandidatesAsync(
        int? customerId,
        int? processId,
        int? machineModelId,
        DataScopeResult scope,
        int? embeddingServiceId,
        bool hydrateEmbeddings = true,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = BuildCandidateSpecQuery(customerId, processId, machineModelId);
        var scopedQuery = ApplySpecScopeToQuery(baseQuery, scope);
        var rawCount = await baseQuery.CountAsync(cancellationToken);
        var scopedCount = await scopedQuery.CountAsync(cancellationToken);
        EnsureCandidateScopeWithinLimit(scopedCount, customerId, processId, machineModelId);

        var scopedSpecs = await scopedQuery
            .Select(spec => new CandidateSpecRow
            {
                Id = spec.Id,
                Project = spec.Project,
                Specification = spec.Specification,
                Acceptance = spec.Acceptance,
                Remark = spec.Remark,
                ImportedAt = spec.ImportedAt
            })
            .ToListAsync(cancellationToken);

        // 重复导入会产生项目+规格相同但内容完整度不同的候选，优先保留信息更完整的新记录。
        var dedupedSpecs = scopedSpecs
            .GroupBy(spec => BuildCandidateDedupKey(spec.Project, spec.Specification))
            .Select(group => group
                .OrderByDescending(spec => HasText(spec.Acceptance))
                .ThenByDescending(spec => HasText(spec.Remark))
                .ThenByDescending(spec => spec.ImportedAt)
                .ThenByDescending(spec => spec.Id)
                .First())
            .ToList();

        _logger.LogInformation(
            "匹配候选去重: 原始{RawCount}条, 范围内{ScopedCount}条 -> 去重后{DedupedCount}条 (customerId={CustomerId}, processId={ProcessId}, machineModelId={MachineModelId})",
            rawCount,
            scopedCount,
            dedupedSpecs.Count,
            customerId,
            processId,
            machineModelId);

        var candidates = dedupedSpecs.Select(spec => new MatchCandidate
        {
            SpecId = spec.Id,
            Project = spec.Project,
            Specification = spec.Specification,
            Acceptance = spec.Acceptance,
            Remark = spec.Remark
        }).ToList();

        if (hydrateEmbeddings)
        {
            await HydrateCandidateEmbeddingsAsync(candidates, embeddingServiceId, cancellationToken);
        }

        return candidates;
    }

    public async Task EnsureEmbeddingServiceConfiguredAsync(
        int? embeddingServiceId,
        CancellationToken cancellationToken = default)
    {
        var configs = await _aiServiceSelector.GetCandidatesAsync(
            CoreAiServicePurpose.Embedding,
            embeddingServiceId,
            cancellationToken);
        if (configs.Count == 0)
        {
            throw Failure("Embedding 服务不可用: 未检测到可用的 Embedding 服务配置");
        }
    }

    public void EnsureEmbeddingServiceAvailable(MatchingConfig config)
    {
        if (config.ExactMatchOnly || _embeddingService.IsAvailable)
        {
            return;
        }

        throw Failure("Embedding 服务不可用: 未检测到可用的 Embedding 服务配置");
    }

    public static string BuildCandidateDedupKey(string? project, string? specification)
    {
        return string.Join(
            "\u001f",
            NormalizeForDedup(project),
            NormalizeForDedup(specification));
    }

    public async Task HydrateCandidateEmbeddingsAsync(
        List<MatchCandidate> candidates,
        int? embeddingServiceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _specEmbeddingCacheService.HydrateMatchingCandidatesAsync(
                candidates,
                embeddingServiceId,
                cancellationToken);
        }
        catch (AiServiceUnavailableException ex)
        {
            _logger.LogWarning(ex, "匹配候选生成 Embedding 失败");
            throw Failure($"Embedding 服务不可用: {ex.Reason}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "匹配候选生成 Embedding 失败");
            throw Failure("Embedding 服务不可用: 匹配候选 Embedding 生成失败");
        }
    }

    private void EnsureCandidateScopeWithinLimit(
        int scopedCount,
        int? customerId,
        int? processId,
        int? machineModelId)
    {
        if (scopedCount <= MaxScopedCandidateCount)
        {
            return;
        }

        _logger.LogWarning(
            "匹配范围候选过多，拒绝继续处理: scopedCount={ScopedCount}, limit={Limit}, customerId={CustomerId}, processId={ProcessId}, machineModelId={MachineModelId}",
            scopedCount,
            MaxScopedCandidateCount,
            customerId,
            processId,
            machineModelId);
        throw Failure($"匹配范围内候选数据过多（{scopedCount}条），请按客户/制程/机型缩小范围后重试");
    }

    private IQueryable<AcceptanceSpec> BuildCandidateSpecQuery(
        int? customerId,
        int? processId,
        int? machineModelId)
    {
        var query = _unitOfWork.AcceptanceSpecs.Query();

        if (customerId.HasValue)
        {
            query = query.Where(spec => spec.CustomerId == customerId.Value);
        }

        if (processId.HasValue)
        {
            query = query.Where(spec => spec.ProcessId == processId.Value);
        }

        if (machineModelId.HasValue)
        {
            query = query.Where(spec => spec.MachineModelId == machineModelId.Value);
        }

        return query;
    }

    private static IQueryable<AcceptanceSpec> ApplySpecScopeToQuery(
        IQueryable<AcceptanceSpec> query,
        DataScopeResult scope)
    {
        if (scope.IsAll)
        {
            return query;
        }

        var scopedOrgUnitIds = scope.OrgUnitIds.Distinct().ToArray();
        if (scope.IncludeSelf && scopedOrgUnitIds.Length > 0)
        {
            return query.Where(spec =>
                (spec.CreatedByUserId.HasValue && spec.CreatedByUserId.Value == scope.UserId) ||
                (spec.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(spec.OwnerOrgUnitId.Value)));
        }

        if (scope.IncludeSelf)
        {
            return query.Where(spec =>
                spec.CreatedByUserId.HasValue && spec.CreatedByUserId.Value == scope.UserId);
        }

        if (scopedOrgUnitIds.Length > 0)
        {
            return query.Where(spec =>
                spec.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(spec.OwnerOrgUnitId.Value));
        }

        return query.Where(_ => false);
    }

    private static string NormalizeForDedup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(" ", value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool HasText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    private static MatchingApiException Failure(string message)
    {
        return new MatchingApiException(400, message);
    }
}
