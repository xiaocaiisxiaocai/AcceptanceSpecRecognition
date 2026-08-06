using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using CoreAiServicePurpose = AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose;

namespace AcceptanceSpecSystem.Application.Services;

public interface IAiServiceReadinessProbeScheduler
{
    void RequestProbe(AiServiceProbeConfig config, CoreAiServicePurpose purpose, long generation);
}

public interface IAiServiceSelectionAppService
{
    Task<AiServiceSelectionDto> GetSelectionAsync(
        AiServicePurpose purpose,
        CancellationToken cancellationToken = default);

    Task<AiServiceSelectionDto> PreloadPreferredAsync(
        AiServicePurpose purpose,
        CancellationToken cancellationToken = default);
}

public sealed class AiServiceSelectionAppService : IAiServiceSelectionAppService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly AiServiceReadinessRegistry _registry;
    private readonly IAiServiceReadinessProbeScheduler _probeScheduler;

    public AiServiceSelectionAppService(
        IUnitOfWork unitOfWork,
        AiServiceReadinessRegistry registry,
        IAiServiceReadinessProbeScheduler probeScheduler)
    {
        _unitOfWork = unitOfWork;
        _registry = registry;
        _probeScheduler = probeScheduler;
    }

    public async Task<AiServiceSelectionDto> GetSelectionAsync(
        AiServicePurpose purpose,
        CancellationToken cancellationToken = default) =>
        await GetSelectionCoreAsync(purpose, probeFallbackCandidates: true, cancellationToken);

    public async Task<AiServiceSelectionDto> PreloadPreferredAsync(
        AiServicePurpose purpose,
        CancellationToken cancellationToken = default) =>
        await GetSelectionCoreAsync(purpose, probeFallbackCandidates: false, cancellationToken);

    private async Task<AiServiceSelectionDto> GetSelectionCoreAsync(
        AiServicePurpose purpose,
        bool probeFallbackCandidates,
        CancellationToken cancellationToken)
    {
        if (purpose is not AiServicePurpose.Llm and not AiServicePurpose.Embedding)
            throw new ApplicationServiceException(400, "purpose 仅支持 llm 或 embedding");

        var entities = await _unitOfWork.AiServiceConfigs.Query()
            .Where(config => !config.IsDisabled)
            .OrderBy(config => config.Priority)
            .ThenByDescending(config => config.UpdatedAt ?? config.CreatedAt)
            .ToListAsync(cancellationToken);
        var candidates = entities
            .Where(entity => SupportsPurpose(entity, purpose))
            .Select(ToProbeConfig)
            .ToList();
        if (candidates.Count == 0)
            return Unavailable("未配置已启用的对应 AI 服务");

        var corePurpose = ToCorePurpose(purpose);
        AiServiceProbeConfig? firstChecking = null;
        DateTime? firstCheckingAt = null;
        foreach (var candidate in candidates)
        {
            var snapshot = _registry.GetSnapshot(candidate.Id, corePurpose);
            if (snapshot.State == AiServiceReadinessState.Available)
                return Available(candidate, purpose, snapshot);

            if (snapshot.State == AiServiceReadinessState.Unknown &&
                _registry.TryMarkChecking(candidate.Id, corePurpose, out var generation))
            {
                _probeScheduler.RequestProbe(candidate, corePurpose, generation);
                snapshot = _registry.GetSnapshot(candidate.Id, corePurpose);
            }

            if (snapshot.State == AiServiceReadinessState.Available)
                return Available(candidate, purpose, snapshot);

            if (snapshot.State == AiServiceReadinessState.Checking && firstChecking == null)
            {
                firstChecking = candidate;
                firstCheckingAt = snapshot.CheckedAt;
            }

            if (!probeFallbackCandidates)
                break;
        }

        if (firstChecking != null)
        {
            return new AiServiceSelectionDto
            {
                Status = "checking",
                ServiceId = firstChecking.Id,
                Name = firstChecking.Name,
                Model = ResolveModel(firstChecking, purpose),
                CheckedAt = firstCheckingAt,
                Message = "正在检测 AI 服务可用性"
            };
        }

        return Unavailable("已启用的 AI 服务当前均不可用，请稍后重试或检查配置");
    }

    private static AiServiceSelectionDto Available(
        AiServiceProbeConfig config,
        AiServicePurpose purpose,
        AiServiceReadinessSnapshot snapshot) => new()
    {
        Status = "available",
        ServiceId = config.Id,
        Name = config.Name,
        Model = ResolveModel(config, purpose),
        CheckedAt = snapshot.CheckedAt,
        Message = snapshot.Message
    };

    private static AiServiceSelectionDto Unavailable(string message) => new()
    {
        Status = "unavailable",
        Message = message
    };

    private static bool SupportsPurpose(AiServiceConfig entity, AiServicePurpose purpose)
    {
        var effectivePurpose = entity.GetEffectivePurpose();
        return purpose == AiServicePurpose.Llm
            ? effectivePurpose.HasFlag(AiServicePurpose.Llm) && !string.IsNullOrWhiteSpace(entity.LlmModel)
            : effectivePurpose.HasFlag(AiServicePurpose.Embedding) && !string.IsNullOrWhiteSpace(entity.EmbeddingModel);
    }

    private static string? ResolveModel(AiServiceProbeConfig config, AiServicePurpose purpose) =>
        purpose == AiServicePurpose.Llm ? config.LlmModel : config.EmbeddingModel;

    private static CoreAiServicePurpose ToCorePurpose(AiServicePurpose purpose) =>
        purpose == AiServicePurpose.Llm ? CoreAiServicePurpose.Llm : CoreAiServicePurpose.Embedding;

    private static AiServiceProbeConfig ToProbeConfig(AiServiceConfig entity) => new(
        entity.Id,
        entity.Name,
        entity.ServiceType,
        entity.GetEffectivePurpose(),
        entity.Priority,
        entity.ApiKey,
        entity.Endpoint,
        entity.EmbeddingModel,
        entity.LlmModel,
        entity.DisableThinking,
        entity.IsDisabled,
        entity.CreatedAt,
        entity.UpdatedAt);
}
