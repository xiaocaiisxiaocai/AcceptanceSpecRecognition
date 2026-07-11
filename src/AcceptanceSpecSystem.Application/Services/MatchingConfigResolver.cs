using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 统一解析匹配配置，避免预览与执行路径各自维护一套默认值和边界裁剪。
/// </summary>
public sealed class MatchingConfigResolver
{
    private readonly IUnitOfWork _unitOfWork;

    public MatchingConfigResolver(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<MatchingConfig> ResolveAsync(
        MatchConfigDto? dto,
        CancellationToken cancellationToken = default)
    {
        var fallbackConfig = new MatchingConfig();
        var defaultRecallTopK = await ResolveDefaultRecallTopKAsync(dto?.EmbeddingServiceId, cancellationToken);

        return new MatchingConfig
        {
            EmbeddingServiceId = dto?.EmbeddingServiceId,
            LlmServiceId = dto?.LlmServiceId,
            MinScoreThreshold = Math.Clamp(dto?.MinScoreThreshold ?? fallbackConfig.MinScoreThreshold, 0, 1),
            HighConfidenceThreshold = MatchingThresholds.NormalizeHighConfidenceThreshold(
                dto?.HighConfidenceThreshold ?? fallbackConfig.HighConfidenceThreshold),
            RecallTopK = Math.Clamp(dto?.RecallTopK ?? defaultRecallTopK, 1, MatchingThresholds.MaxRecallTopK),
            AmbiguityMargin = Math.Clamp(dto?.AmbiguityMargin ?? fallbackConfig.AmbiguityMargin, 0, 1),
            LlmParallelism = Math.Clamp(dto?.LlmParallelism ?? fallbackConfig.LlmParallelism, 1, 10),
            LlmRowTimeoutSeconds = Math.Clamp(dto?.LlmRowTimeoutSeconds ?? fallbackConfig.LlmRowTimeoutSeconds, 5, 300),
            LlmRetryCount = Math.Clamp(dto?.LlmRetryCount ?? fallbackConfig.LlmRetryCount, 0, 3),
            LlmCircuitBreakFailures = Math.Clamp(dto?.LlmCircuitBreakFailures ?? fallbackConfig.LlmCircuitBreakFailures, 3, 200),
            MatchingMode = ParseMatchingMode(dto?.MatchingMode, fallbackConfig.MatchingMode),
            EnableLlmEquivalenceAdjudication = dto?.EnableLlmEquivalenceAdjudication ?? fallbackConfig.EnableLlmEquivalenceAdjudication,
            EnableDeterministicAutoApply = dto?.EnableDeterministicAutoApply ?? fallbackConfig.EnableDeterministicAutoApply,
            LlmEquivalenceMinConfidence = Math.Clamp(
                dto?.LlmEquivalenceMinConfidence ?? fallbackConfig.LlmEquivalenceMinConfidence, 0, 1),
            LlmMaxCallsPerBatch = Math.Clamp(dto?.LlmMaxCallsPerBatch ?? fallbackConfig.LlmMaxCallsPerBatch, 0, 10000),
            ExactMatchOnly = dto?.ExactMatchOnly ?? fallbackConfig.ExactMatchOnly,
            FilterEmptySourceRows = dto?.FilterEmptySourceRows ?? fallbackConfig.FilterEmptySourceRows,
            EnableLlmSemanticPriority = dto?.EnableLlmSemanticPriority ?? fallbackConfig.EnableLlmSemanticPriority,
            LlmSemanticRecallThreshold = Math.Clamp(
                dto?.LlmSemanticRecallThreshold ?? fallbackConfig.LlmSemanticRecallThreshold, 0.1, 0.9),
            EmbeddingSemanticAutoApplyThreshold = Math.Clamp(
                dto?.EmbeddingSemanticAutoApplyThreshold ?? fallbackConfig.EmbeddingSemanticAutoApplyThreshold, 0, 1)
        };
    }

    private static MatchingMode ParseMatchingMode(string? value, MatchingMode fallback)
    {
        return value?.Trim() switch
        {
            "specificationOnly" => MatchingMode.SpecificationOnly,
            "projectSpecification" or null or "" => fallback,
            _ => fallback
        };
    }

    private async Task<int> ResolveDefaultRecallTopKAsync(
        int? embeddingServiceId,
        CancellationToken cancellationToken)
    {
        var fallbackConfig = new MatchingConfig();
        var query = _unitOfWork.AiServiceConfigs
            .Query()
            .AsNoTracking()
            .Where(item =>
                !item.IsDisabled &&
                (item.Purpose & AiServicePurpose.Embedding) == AiServicePurpose.Embedding);

        AiServiceConfig? embeddingService;
        if (embeddingServiceId.HasValue)
        {
            embeddingService = await query.FirstOrDefaultAsync(
                item => item.Id == embeddingServiceId.Value,
                cancellationToken);
        }
        else
        {
            embeddingService = await query
                .OrderBy(item => item.Priority)
                .ThenByDescending(item => item.UpdatedAt ?? item.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return embeddingService?.DefaultRecallTopK ?? fallbackConfig.RecallTopK;
    }
}
