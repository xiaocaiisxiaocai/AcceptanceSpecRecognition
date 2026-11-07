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

public sealed partial class MatchingWorkflowSupportService
{
    internal async Task RunLlmStreamAsync(MatchingUserContext user, IMatchingEventStream response, MatchLlmStreamRequest request, CancellationToken cancellationToken)
    {
        if (request.Items == null || request.Items.Count == 0)
        {
            throw Failure(400, "Items不能为空");
        }

        EnsureDistinctLlmStreamItems(request.Items);

        var scope = await ResolveSpecScopeAsync(user, cancellationToken);
        if (scope == null)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        DataScopeResult businessScope;
        if (request.FileId.HasValue && request.FileId.Value > 0)
        {
            var wordFile = await _documentFileAccessService.GetAccessibleWordFileAsync(
                request.FileId.Value,
                scope);
            if (wordFile == null)
            {
                throw Failure(400, "源文件不存在");
            }

            businessScope = await _businessOrgScopeService.ResolveFileScopeAsync(
                scope,
                wordFile,
                cancellationToken);
        }
        else
        {
            businessScope = await _businessOrgScopeService.ResolveCurrentScopeAsync(
                scope,
                user.IsAdmin,
                cancellationToken);
        }

        var config = await _matchingConfigResolver.ResolveAsync(request.Config, cancellationToken);
        var candidates = await _matchingCandidateProvider.GetCandidatesAsync(
            request.CustomerId,
            request.ProcessId,
            request.MachineModelId,
            businessScope,
            config.EmbeddingServiceId,
            hydrateEmbeddings: false,
            cancellationToken: cancellationToken);
        var accessibleSpecLookup = candidates.ToDictionary(candidate => candidate.SpecId);
        var normalizedItems = await BuildAuthoritativeLlmStreamItemsAsync(request.Items, candidates, config, cancellationToken);

        response.Prepare();

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
                                businessScope,
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
            if (!requestAborted && !response.IsClientDisconnected)
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
}
