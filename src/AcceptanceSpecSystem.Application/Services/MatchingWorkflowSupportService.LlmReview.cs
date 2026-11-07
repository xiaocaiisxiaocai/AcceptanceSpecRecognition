using System.Collections.Concurrent;
using static AcceptanceSpecSystem.Application.Services.MatchingResultHelpers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Diagnostics;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 流式 LLM 复核：单行复核的流式输出与复核结果克隆（从 LlmStream 拆出，保持单文件体量）。
/// </summary>
public sealed partial class MatchingWorkflowSupportService
{
    private async Task<LlmStepOutcome> StreamLlmReviewAsync(
        IMatchingEventStream response,
        MatchLlmStreamItem item,
        MatchResultDto? authoritativeBestMatch,
        MatchingConfig config,
        DataScopeResult scope,
        int? customerId,
        int? processId,
        int? machineModelId,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<int, MatchCandidate> accessibleSpecLookup,
        ILlmReviewService reviewService,
        ConcurrentDictionary<LlmStreamItemKey, byte> reviewTerminalLookup,
        SemaphoreSlim sseWriteLock)
    {
        var specId = item.BestMatchSpecId ?? 0;
        if (specId <= 0)
            return LlmStepOutcome.Failed;

        var location = FormatStreamItemLocation(item);

        if (RequiresManualReviewByEquivalenceVerdict(item.LlmEquivalenceVerdict))
        {
            await WriteSseEventLockedAsync(response, sseWriteLock, "review.done", new
            {
                tableIndex = item.TableIndex,
                rowIndex = item.RowIndex,
                score = 0,
                reason = "AI 等价裁决已要求人工确认，跳过旧复核",
                commentary = "保留 AI 等价裁决结果，不再用旧 LLM 复核反向放行",
                decision = "manualReview",
                bestMatch = CloneReviewedBestMatch(
                    authoritativeBestMatch,
                    "manualReview",
                    0,
                    "AI 等价裁决已要求人工确认，跳过旧复核",
                    "保留 AI 等价裁决结果，不再用旧 LLM 复核反向放行",
                    reviewApprovalToken: null)
            }, cancellationToken);
            reviewTerminalLookup.TryAdd(GetLlmStreamItemKey(item), 0);
            return LlmStepOutcome.Success;
        }

        if (!item.IsAmbiguous)
        {
            return LlmStepOutcome.Success;
        }

        if (!accessibleSpecLookup.TryGetValue(specId, out var spec))
        {
            _logger.LogWarning("[LLM复核] {Location}: 最佳匹配规格ID={SpecId}不存在或无权限", location, specId);
            throw new LlmStepFailureException("最佳匹配规格不存在或无权限", "manualReview");
        }

        _logger.LogDebug(
            "[LLM复核] {Location}: sourceRow={RowIndex}, candidateSpecId={SpecId}, baseScore={Score:P1}, traceId={TraceId}",
            location,
            item.RowIndex,
            specId,
            item.BestMatchScore ?? 0,
            Activity.Current?.TraceId.ToString());

        var reviewRequest = new LlmReviewRequest
        {
            SourceProject = item.SourceProject,
            SourceSpecification = item.SourceSpecification,
            BestMatchProject = spec.Project,
            BestMatchSpecification = spec.Specification,
            BestMatchAcceptance = spec.Acceptance,
            BestMatchRemark = spec.Remark,
            BaseScore = (item.BestMatchScore ?? 0) * 100,
            ScoreDetails = item.ScoreDetails ?? new Dictionary<string, double>(),
            CurrentDecision = item.Decision ?? "manualReview",
            EvidenceSummary = item.EvidenceSummary ?? [],
            ConflictSummary = item.ConflictSummary ?? [],
            ReviewTrigger = BuildReviewTrigger(item),
            LlmServiceId = config.LlmServiceId,
            ReviewScene = LlmReviewScene.MatchingReview
        };

        await WriteSseEventLockedAsync(response, sseWriteLock, "review.start", new
        {
            tableIndex = item.TableIndex,
            rowIndex = item.RowIndex
        }, cancellationToken);

        var buffer = new StringBuilder();
        try
        {
            await foreach (var chunk in reviewService.ReviewStreamAsync(reviewRequest, cancellationToken))
            {
                buffer.Append(chunk);
                await WriteSseEventLockedAsync(response, sseWriteLock, "review.delta", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    chunk
                }, cancellationToken);
            }

            if (reviewService.TryParseReviewResult(buffer.ToString(), out var result))
            {
                var normalizedScore = NormalizeLlmReviewScore(result.Score);
                var passed = normalizedScore >= MatchingThresholds.LlmReviewPassScore;
                var reviewApprovalToken = passed
                    ? _approvalTokenService.IssueToken(
                        scope.UserId,
                        item.TableIndex,
                        item.RowIndex,
                        item.BestMatchSpecId ?? 0,
                        item.SourceProject,
                        item.SourceSpecification,
                        spec.Project,
                        spec.Specification,
                        spec.Acceptance,
                        spec.Remark,
                        customerId,
                        processId,
                        machineModelId,
                        config)
                    : null;
                _logger.LogDebug(
                    "[LLM复核] {Location}: 完成, score={Score}, decision={Decision}, traceId={TraceId}",
                    location,
                    normalizedScore,
                    passed ? "autoApply" : "manualReview",
                    Activity.Current?.TraceId.ToString());
                await WriteSseEventLockedAsync(response, sseWriteLock, "review.done", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    score = normalizedScore,
                    reason = result.Reason,
                    commentary = result.Commentary,
                    decision = passed ? "autoApply" : "manualReview",
                    reviewApprovalToken,
                    bestMatch = CloneReviewedBestMatch(
                        authoritativeBestMatch,
                        passed ? "autoApply" : "manualReview",
                        normalizedScore,
                        result.Reason,
                        result.Commentary,
                        reviewApprovalToken)
                }, cancellationToken);
                reviewTerminalLookup.TryAdd(GetLlmStreamItemKey(item), 0);
                return LlmStepOutcome.Success;
            }
            else
            {
                _logger.LogWarning("[LLM复核] {Location}: JSON解析失败, 输出摘要: {Summary}",
                    location,
                    SensitiveLogFormatter.DescribePayload(buffer.ToString()));
                throw new LlmStepFailureException("LLM复核输出解析失败", "manualReview");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AiServiceUnavailableException ex)
        {
            _logger.LogWarning(
                "LLM复核失败: exceptionType={ExceptionType}, traceId={TraceId}",
                ex.GetType().Name,
                Activity.Current?.TraceId.ToString());
            throw new LlmStepFailureException(ex.Reason, "manualReview", ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "LLM复核失败: exceptionType={ExceptionType}, traceId={TraceId}",
                ex.GetType().Name,
                Activity.Current?.TraceId.ToString());
            if (ex is LlmStepFailureException)
            {
                throw;
            }

            throw new LlmStepFailureException("LLM复核失败", "manualReview", ex);
        }
    }

    private static MatchResultDto? CloneReviewedBestMatch(
        MatchResultDto? bestMatch,
        string decision,
        double? reviewScore,
        string? reviewReason,
        string? reviewCommentary,
        string? reviewApprovalToken)
    {
        if (bestMatch == null)
        {
            return null;
        }

        return new MatchResultDto
        {
            SpecId = bestMatch.SpecId,
            Project = bestMatch.Project,
            Specification = bestMatch.Specification,
            Acceptance = bestMatch.Acceptance,
            Remark = bestMatch.Remark,
            Score = bestMatch.Score,
            EmbeddingScore = bestMatch.EmbeddingScore,
            ScoreDetails = new Dictionary<string, double>(bestMatch.ScoreDetails),
            Decision = decision,
            EvidenceSummary = [.. bestMatch.EvidenceSummary],
            ConflictSummary = [.. bestMatch.ConflictSummary],
            Issues = bestMatch.Issues.Select(issue => new MatchIssueDto
            {
                Code = issue.Code,
                Severity = issue.Severity,
                FieldName = issue.FieldName,
                SourceValue = issue.SourceValue,
                CandidateValue = issue.CandidateValue,
                Message = issue.Message,
                SuggestedAction = issue.SuggestedAction
            }).ToList(),
            Entities = bestMatch.Entities.Select(entity => new MatchEntityEvidenceDto
            {
                EntityType = entity.EntityType,
                SourceValue = entity.SourceValue,
                CandidateValue = entity.CandidateValue,
                NormalizedSourceValue = entity.NormalizedSourceValue,
                NormalizedCandidateValue = entity.NormalizedCandidateValue,
                Relation = entity.Relation
            }).ToList(),
            TopCandidates = bestMatch.TopCandidates.Select(candidate => new MatchCandidateDto
            {
                Rank = candidate.Rank,
                SpecId = candidate.SpecId,
                Project = candidate.Project,
                Specification = candidate.Specification,
                Acceptance = candidate.Acceptance,
                Remark = candidate.Remark,
                Score = candidate.Score,
                EmbeddingScore = candidate.EmbeddingScore,
                ScoreDetails = new Dictionary<string, double>(candidate.ScoreDetails),
                Decision = candidate.Decision,
                EvidenceSummary = [.. candidate.EvidenceSummary],
                ConflictSummary = [.. candidate.ConflictSummary],
                Issues = candidate.Issues.Select(issue => new MatchIssueDto
                {
                    Code = issue.Code,
                    Severity = issue.Severity,
                    FieldName = issue.FieldName,
                    SourceValue = issue.SourceValue,
                    CandidateValue = issue.CandidateValue,
                    Message = issue.Message,
                    SuggestedAction = issue.SuggestedAction
                }).ToList(),
                Entities = candidate.Entities.Select(entity => new MatchEntityEvidenceDto
                {
                    EntityType = entity.EntityType,
                    SourceValue = entity.SourceValue,
                    CandidateValue = entity.CandidateValue,
                    NormalizedSourceValue = entity.NormalizedSourceValue,
                    NormalizedCandidateValue = entity.NormalizedCandidateValue,
                    Relation = entity.Relation
                }).ToList(),
                RerankSummary = candidate.RerankSummary,
                SelectionMode = candidate.SelectionMode,
                SelectionSummary = candidate.SelectionSummary,
                MatchBasis = candidate.MatchBasis,
                LlmEquivalence = candidate.LlmEquivalence
            }).ToList(),
            RecalledCandidateCount = bestMatch.RecalledCandidateCount,
            IsAmbiguous = bestMatch.IsAmbiguous,
            ScoreGap = bestMatch.ScoreGap,
            RerankSummary = bestMatch.RerankSummary,
            SelectionMode = bestMatch.SelectionMode,
            SelectionSummary = bestMatch.SelectionSummary,
            MatchBasis = bestMatch.MatchBasis,
            LlmEquivalence = bestMatch.LlmEquivalence,
            ReviewApprovalToken = reviewApprovalToken,
            ReviewScore = reviewScore,
            ReviewReason = reviewReason,
            ReviewCommentary = reviewCommentary
        };
    }
}
