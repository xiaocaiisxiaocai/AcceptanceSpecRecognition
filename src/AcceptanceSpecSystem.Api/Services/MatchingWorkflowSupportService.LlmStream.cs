using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Diagnostics;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Http;

namespace AcceptanceSpecSystem.Api.Services;

public sealed partial class MatchingWorkflowSupportService
{
    internal async Task RunLlmStreamAsync(ClaimsPrincipal user, HttpResponse response, MatchLlmStreamRequest request, CancellationToken cancellationToken)
    {
        if (request.Items == null || request.Items.Count == 0)
        {
            throw Failure(400, "Items不能为空");
        }

        EnsureDistinctLlmStreamItems(request.Items);

        var scope = await ResolveSpecScopeAsync(user);
        if (scope == null)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        var config = await _matchingConfigResolver.ResolveAsync(request.Config, cancellationToken);
        var candidates = await _matchingCandidateProvider.GetCandidatesAsync(
            request.CustomerId,
            request.ProcessId,
            request.MachineModelId,
            scope,
            config.EmbeddingServiceId,
            hydrateEmbeddings: false,
            cancellationToken);
        var accessibleSpecLookup = candidates.ToDictionary(candidate => candidate.SpecId);
        var normalizedItems = await BuildAuthoritativeLlmStreamItemsAsync(request.Items, candidates, config, cancellationToken);

        response.Headers.CacheControl = "no-cache";
        response.Headers.TryAdd("X-Accel-Buffering", "no");
        response.ContentType = "text/event-stream";

        // 并行处理：每行独立创建 DI 作用域（DbContext 非线程安全），SSE 写入用信号量串行化
        var sseWriteLock = new SemaphoreSlim(1, 1);
        var sw = Stopwatch.StartNew();

        var parallelism = config.LlmParallelism;
        var rowTimeoutSeconds = config.LlmRowTimeoutSeconds;
        var retryCount = config.LlmRetryCount;
        var circuitBreakFailures = config.LlmCircuitBreakFailures;
        var reviewTargetLookup = normalizedItems.ToDictionary(
            context => GetLlmStreamItemKey(context.Item),
            context => RequiresReviewForStreamItem(context.Item));
        var reviewCount = reviewTargetLookup.Count(item => item.Value);
        var reviewTerminalLookup = new ConcurrentDictionary<LlmStreamItemKey, byte>();
        var reviewSuccess = 0;
        var reviewFailed = 0;
        var reviewTimeout = 0;
        var reviewRetries = 0;
        var totalFailures = 0;
        var circuitOpened = 0;
        var requestAborted = false;

        _logger.LogInformation(
            "[LLM-Stream] 开始并行处理 {Count} 行 (review={ReviewCount}, maxParallelism={Parallelism}, rowTimeoutSec={RowTimeoutSec}, retryCount={RetryCount}, circuitBreakFailures={CircuitBreakFailures})",
            normalizedItems.Count, reviewCount, parallelism,
            rowTimeoutSeconds, retryCount, circuitBreakFailures);

        try
        {
            await Parallel.ForEachAsync(
                normalizedItems,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = parallelism,
                    CancellationToken = cancellationToken
                },
                async (context, ct) =>
                {
                    using var serviceScope = _scopeFactory.CreateScope();
                    var reviewService = serviceScope.ServiceProvider.GetRequiredService<ILlmReviewService>();
                    var item = context.Item;
                    var location = FormatStreamItemLocation(item);
                    var itemKey = GetLlmStreamItemKey(item);
                    var requiresReview = reviewTargetLookup.GetValueOrDefault(itemKey);

                    if (Volatile.Read(ref circuitOpened) == 1)
                    {
                        await WriteCircuitOpenEventsAsync(response, item, requiresReview, reviewTerminalLookup, sseWriteLock, ct);
                        return;
                    }

                    if (requiresReview)
                    {
                        _logger.LogDebug("[LLM-Stream] {Location}: 开始复核 (specId={SpecId}, score={Score:P1})",
                            location, item.BestMatchSpecId, item.BestMatchScore ?? 0);

                        var reviewResult = await ExecuteLlmStepWithPolicyAsync(
                            response,
                            "review",
                            item,
                            rowTimeoutSeconds,
                            retryCount,
                            token => StreamLlmReviewAsync(
                                response,
                                item,
                                context.AuthoritativeBestMatch,
                                config,
                                scope,
                                request.CustomerId,
                                request.ProcessId,
                                request.MachineModelId,
                                token,
                                accessibleSpecLookup,
                                reviewService,
                                reviewTerminalLookup,
                                sseWriteLock),
                            reviewTerminalLookup,
                            sseWriteLock,
                            ct);

                        Interlocked.Add(ref reviewRetries, reviewResult.RetriesUsed);
                        switch (reviewResult.Outcome)
                        {
                            case LlmStepOutcome.Success:
                                Interlocked.Increment(ref reviewSuccess);
                                break;
                            case LlmStepOutcome.Timeout:
                                Interlocked.Increment(ref reviewTimeout);
                                var timeoutFailures = Interlocked.Increment(ref totalFailures);
                                var openedByTimeout = timeoutFailures >= circuitBreakFailures &&
                                                     Interlocked.Exchange(ref circuitOpened, 1) == 0;
                                if (openedByTimeout)
                                {
                                    return;
                                }
                                break;
                            default:
                                Interlocked.Increment(ref reviewFailed);
                                var failedCount = Interlocked.Increment(ref totalFailures);
                                var openedByFailure = failedCount >= circuitBreakFailures &&
                                                      Interlocked.Exchange(ref circuitOpened, 1) == 0;
                                if (openedByFailure)
                                {
                                    return;
                                }
                                break;
                        }
                    }

                    if (ct.IsCancellationRequested) return;
                    if (Volatile.Read(ref circuitOpened) == 1)
                    {
                        await WriteCircuitOpenEventsAsync(response, item, requiresReview, reviewTerminalLookup, sseWriteLock, ct);
                        return;
                    }
                });
        }
        catch (OperationCanceledException)
        {
            requestAborted = true;
            _logger.LogDebug("LLM 流式输出：客户端已断开连接");
        }
        finally
        {
            if (!requestAborted && !response.HttpContext.RequestAborted.IsCancellationRequested)
            {
                await WriteSseEventSafeAsync(response, "stream.complete", new
                {
                    completedRowKeys = normalizedItems
                        .Select(context => FormatStreamRowKey(context.Item.TableIndex, context.Item.RowIndex))
                        .ToArray(),
                    totalItems = normalizedItems.Count,
                    reviewTargets = reviewCount,
                    reviewSuccess,
                    reviewFailed,
                    reviewTimeout,
                    reviewRetries,
                    totalFailures,
                    circuitOpened = circuitOpened == 1,
                    elapsedMs = sw.ElapsedMilliseconds
                }, CancellationToken.None);
            }

            sseWriteLock.Dispose();
        }

        _logger.LogInformation(
            "[LLM-Stream] 全部完成, 耗时 {Elapsed}ms, review(success={ReviewSuccess}, failed={ReviewFailed}, timeout={ReviewTimeout}, retries={ReviewRetries}), totalFailures={TotalFailures}, circuitOpened={CircuitOpened}",
            sw.ElapsedMilliseconds,
            reviewSuccess, reviewFailed, reviewTimeout, reviewRetries,
            totalFailures, circuitOpened == 1);
    }



    private async Task<LlmStepOutcome> StreamLlmReviewAsync(
        HttpResponse response,
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
            "[LLM复核] {Location}: 源=[{SrcProj}/{SrcSpec}] 匹配=[{MatchProj}/{MatchSpec}] 基础得分={Score:P1}",
            location, item.SourceProject, item.SourceSpecification,
            spec.Project, spec.Specification, item.BestMatchScore ?? 0);

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
                _logger.LogDebug("[LLM复核] {Location}: 完成, score={Score}, reason={Reason}",
                    location, normalizedScore, result.Reason);
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
            _logger.LogWarning(ex, "LLM复核失败");
            throw new LlmStepFailureException(ex.Reason, "manualReview", ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM复核失败");
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
