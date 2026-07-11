using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class MatchingWorkflowSupportService
{
    private async Task WriteCircuitOpenEventsAsync(
        IMatchingEventStream response,
        MatchLlmStreamItem item,
        bool requiresReview,
        ConcurrentDictionary<LlmStreamItemKey, byte> reviewTerminalLookup,
        SemaphoreSlim sseWriteLock,
        CancellationToken cancellationToken)
    {
        const string message = "LLM 失败率过高，已触发熔断，请稍后重试";
        var itemKey = GetLlmStreamItemKey(item);
        if (requiresReview && reviewTerminalLookup.TryAdd(itemKey, 0))
        {
            await WriteSseEventLockedAsync(response, sseWriteLock, "review.error", new
            {
                tableIndex = item.TableIndex,
                rowIndex = item.RowIndex,
                message,
                decision = "manualReview"
            }, cancellationToken);
        }
    }

    /// <summary>
    /// 按统一策略执行单个 LLM 步骤：请求取消直接上抛，步骤超时/失败按配置重试，最终失败时写入终态 SSE。
    /// </summary>
    private async Task<LlmStepExecutionResult> ExecuteLlmStepWithPolicyAsync(
        IMatchingEventStream response,
        string stepName,
        MatchLlmStreamItem item,
        int timeoutSeconds,
        int retryCount,
        Func<CancellationToken, Task<LlmStepOutcome>> executeAsync,
        ConcurrentDictionary<LlmStreamItemKey, byte> reviewTerminalLookup,
        SemaphoreSlim sseWriteLock,
        CancellationToken requestCancellationToken)
    {
        var itemKey = GetLlmStreamItemKey(item);
        for (var attempt = 0; attempt <= retryCount; attempt++)
        {
            using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken);
            stepCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                var outcome = await executeAsync(stepCts.Token);
                if (outcome == LlmStepOutcome.Success || attempt >= retryCount)
                {
                    return new LlmStepExecutionResult(outcome, attempt);
                }

                _logger.LogDebug("[LLM-Stream] {Location}: {Step} 第 {Attempt} 次失败，准备重试",
                    FormatStreamItemLocation(item), stepName, attempt + 1);
            }
            catch (OperationCanceledException) when (requestCancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                if (attempt < retryCount)
                {
                    _logger.LogDebug("[LLM-Stream] {Location}: {Step} 第 {Attempt} 次超时，准备重试",
                        FormatStreamItemLocation(item), stepName, attempt + 1);
                    continue;
                }

                await WriteSseEventLockedAsync(response, sseWriteLock, $"{stepName}.error", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    message = $"{GetLlmStepDisplayName(stepName)}超时（>{timeoutSeconds}s）",
                    decision = string.Equals(stepName, "review", StringComparison.OrdinalIgnoreCase)
                        ? "manualReview"
                        : null
                }, requestCancellationToken);
                reviewTerminalLookup.TryAdd(itemKey, 0);
                return new LlmStepExecutionResult(LlmStepOutcome.Timeout, attempt);
            }
            catch (LlmStepFailureException ex)
            {
                if (attempt < retryCount)
                {
                    _logger.LogWarning(ex, "[LLM-Stream] {Location}: {Step} 第 {Attempt} 次失败，准备重试",
                        FormatStreamItemLocation(item), stepName, attempt + 1);
                    continue;
                }

                _logger.LogWarning(ex, "[LLM-Stream] {Location}: {Step} 重试后仍失败",
                    FormatStreamItemLocation(item), stepName);
                await WriteSseEventLockedAsync(response, sseWriteLock, $"{stepName}.error", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    message = ex.EventMessage,
                    decision = ex.Decision ?? (string.Equals(stepName, "review", StringComparison.OrdinalIgnoreCase)
                        ? "manualReview"
                        : null)
                }, requestCancellationToken);
                reviewTerminalLookup.TryAdd(itemKey, 0);
                return new LlmStepExecutionResult(LlmStepOutcome.Failed, attempt);
            }
            catch (Exception ex)
            {
                if (attempt < retryCount)
                {
                    _logger.LogWarning(ex, "[LLM-Stream] {Location}: {Step} 第 {Attempt} 次异常，准备重试",
                        FormatStreamItemLocation(item), stepName, attempt + 1);
                    continue;
                }

                _logger.LogWarning(ex, "[LLM-Stream] {Location}: {Step} 重试后仍失败",
                    FormatStreamItemLocation(item), stepName);
                await WriteSseEventLockedAsync(response, sseWriteLock, $"{stepName}.error", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    message = $"{GetLlmStepDisplayName(stepName)}失败（已达到重试上限）",
                    decision = string.Equals(stepName, "review", StringComparison.OrdinalIgnoreCase)
                        ? "manualReview"
                        : null
                }, requestCancellationToken);
                reviewTerminalLookup.TryAdd(itemKey, 0);
                return new LlmStepExecutionResult(LlmStepOutcome.Failed, attempt);
            }
        }

        return new LlmStepExecutionResult(LlmStepOutcome.Failed, retryCount);
    }

    private static string GetLlmStepDisplayName(string stepName)
    {
        return string.Equals(stepName, "review", StringComparison.OrdinalIgnoreCase)
            ? "LLM复核"
            : stepName;
    }
}
