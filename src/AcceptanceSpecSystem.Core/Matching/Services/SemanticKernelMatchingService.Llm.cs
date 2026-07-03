using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using Microsoft.Extensions.Logging;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public partial class SemanticKernelMatchingService : IMatchingService
{
    private async Task<LlmCallExecution<T>> ExecuteLlmCallWithPolicyAsync<T>(
        string stepName,
        string location,
        MatchingConfig config,
        LlmCallBudget llmBudget,
        LlmCircuitBreaker circuitBreaker,
        Func<CancellationToken, Task<T?>> executeAsync,
        CancellationToken cancellationToken)
    {
        var retryCount = Math.Clamp(config.LlmRetryCount, 0, 3);
        var timeoutSeconds = Math.Clamp(config.LlmRowTimeoutSeconds, 5, 300);

        // 整行（含所有重试）只扣一次全局预算，重试属于同一次调用的容错而非新调用。
        if (!llmBudget.TryConsume())
        {
            _logger.LogWarning("{StepName} 调用已达批次上限，跳过 LLM 调用: {Location}", stepName, location);
            return new LlmCallExecution<T>(default, Failed: true, BudgetExhausted: true);
        }

        for (var attempt = 0; attempt <= retryCount; attempt++)
        {
            if (circuitBreaker.IsOpen)
            {
                _logger.LogWarning("{StepName} 已熔断，跳过 LLM 调用: {Location}", stepName, location);
                return new LlmCallExecution<T>(default, Failed: true, BudgetExhausted: false);
            }

            using var callCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            callCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                var result = await executeAsync(callCts.Token);
                if (result != null)
                {
                    circuitBreaker.RecordSuccess();
                    return new LlmCallExecution<T>(result, Failed: false, BudgetExhausted: false);
                }

                if (attempt >= retryCount)
                {
                    // 调用成功但输出不可解析：计入熔断（服务质量问题），Failed=false 以区分网络/超时失败
                    circuitBreaker.RecordFailure();
                    return new LlmCallExecution<T>(default, Failed: false, BudgetExhausted: false);
                }

                _logger.LogDebug(
                    "{StepName} 第 {Attempt} 次未返回有效结果，准备重试: {Location}",
                    stepName,
                    attempt + 1,
                    location);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                if (attempt >= retryCount)
                {
                    circuitBreaker.RecordFailure();
                    _logger.LogWarning("{StepName} 超时（>{TimeoutSeconds}s）: {Location}", stepName, timeoutSeconds, location);
                    return new LlmCallExecution<T>(default, Failed: true, BudgetExhausted: false);
                }

                _logger.LogDebug(
                    "{StepName} 第 {Attempt} 次超时，准备重试: {Location}",
                    stepName,
                    attempt + 1,
                    location);
            }
            catch (Exception ex)
            {
                if (attempt >= retryCount)
                {
                    circuitBreaker.RecordFailure();
                    _logger.LogWarning(ex, "{StepName} 重试后仍失败: {Location}", stepName, location);
                    return new LlmCallExecution<T>(default, Failed: true, BudgetExhausted: false);
                }

                _logger.LogWarning(
                    ex,
                    "{StepName} 第 {Attempt} 次失败，准备重试: {Location}",
                    stepName,
                    attempt + 1,
                    location);
            }
        }

        // 循环内所有终止路径均已 return，此处仅为编译器可达性兜底
        circuitBreaker.RecordFailure();
        return new LlmCallExecution<T>(default, Failed: true, BudgetExhausted: false);
    }

    private async Task<EvaluatedCandidate> SelectCurrentBestCandidateAsync(
        MatchSource source,
        IReadOnlyList<EvaluatedCandidate> orderedCandidates,
        MatchingConfig config,
        LlmCallBudget llmBudget,
        LlmCircuitBreaker llmCircuitBreaker,
        CancellationToken cancellationToken)
    {
        var localBest = orderedCandidates[0];
        localBest.SelectionSummary ??= "沿用本地 Top1 排序结果";

        if (_llmCandidateRerankService == null || orderedCandidates.Count <= 1)
        {
            return localBest;
        }

        if (!ShouldRunAiRerank(orderedCandidates, config.AmbiguityMargin))
        {
            localBest.SelectionSummary = "本地 Top1 项目精确命中且优势明确，跳过 AI 重排";
            return localBest;
        }

        if (llmCircuitBreaker.IsOpen)
        {
            localBest.SelectionSummary = AppendReason(
                localBest.SelectionSummary,
                "LLM 失败率过高，已触发熔断，跳过 AI 重排");
            return localBest;
        }

        try
        {
            var rerankRequest = new LlmCandidateRerankRequest
            {
                SourceProject = source.Project,
                SourceSpecification = source.Specification,
                CurrentTopCandidateSpecId = localBest.Candidate.SpecId,
                LlmServiceId = config.LlmServiceId,
                Candidates = orderedCandidates
                    .Select((candidate, index) => new LlmCandidateRerankCandidate
                    {
                        Rank = index + 1,
                        SpecId = candidate.Candidate.SpecId,
                        Project = candidate.Candidate.Project,
                        Specification = candidate.Candidate.Specification,
                        EmbeddingScore = candidate.EmbeddingScore,
                        FinalScore = candidate.FinalScore,
                        ScoreDetails = CreateScoreDetails(candidate),
                        EvidenceSummary = [.. (candidate.Evidence?.Summary ?? [])],
                        ConflictSummary = [.. (candidate.Evidence?.Conflicts ?? [])]
                    })
                    .ToList()
            };
            var rerankExecution = await ExecuteLlmCallWithPolicyAsync(
                "AI 重排",
                $"{source.Project}/{source.Specification}",
                config,
                llmBudget,
                llmCircuitBreaker,
                token => _llmCandidateRerankService.RerankAsync(rerankRequest, token),
                cancellationToken);
            var rerankResult = rerankExecution.Result;

            if (rerankResult == null)
            {
                localBest.SelectionSummary = rerankExecution.BudgetExhausted
                    ? "LLM 调用已达批次上限，跳过 AI 重排，沿用本地 Top1"
                    : "AI 重排未返回有效结果，已沿用本地 Top1";
                return localBest;
            }

            var selected = orderedCandidates.FirstOrDefault(candidate =>
                candidate.Candidate.SpecId == rerankResult.SelectedSpecId);
            if (selected == null)
            {
                localBest.SelectionSummary = "AI 重排返回非法候选，已沿用本地 Top1";
                return localBest;
            }

            if (!ShouldAcceptAiRerankSelection(localBest, selected))
            {
                localBest.SelectionSummary = "AI 重排候选与本地项目一致性冲突，已沿用本地 Top1";
                return localBest;
            }

            if (selected.Candidate.SpecId == localBest.Candidate.SpecId)
            {
                localBest.SelectionSummary = string.IsNullOrWhiteSpace(rerankResult.Reason)
                    ? "AI 重排确认沿用本地 Top1"
                    : $"AI 重排确认沿用本地 Top1：{rerankResult.Reason}";
                return localBest;
            }

            selected.SelectionMode = MatchSelectionMode.AiRerank;
            selected.SelectionSummary = BuildAiRerankSelectionSummary(orderedCandidates, selected, rerankResult);
            return selected;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI 候选重排失败，已沿用本地 Top1");
            localBest.SelectionSummary = "AI 重排失败，已沿用本地 Top1";
            return localBest;
        }
    }

    private static bool ShouldAcceptAiRerankSelection(EvaluatedCandidate localBest, EvaluatedCandidate selected)
    {
        if (selected.Candidate.SpecId == localBest.Candidate.SpecId)
            return true;

        var localBestHasExactProject =
            localBest.ProjectScore >= ExactTextMatchThreshold &&
            localBest.ProjectCodeConflictPenalty <= 0;
        var selectedHasProjectCodeConflict = selected.ProjectCodeConflictPenalty > 0;

        if (localBestHasExactProject &&
            selectedHasProjectCodeConflict &&
            localBest.FinalScore >= selected.FinalScore - ScoreTieEpsilon)
        {
            return false;
        }

        return true;
    }

    private static bool ShouldRunAiRerank(
        IReadOnlyList<EvaluatedCandidate> orderedCandidates,
        double ambiguityMargin)
    {
        if (orderedCandidates.Count <= 1)
            return false;

        var localBest = orderedCandidates[0];
        var second = orderedCandidates[1];
        var localBestHasExactProject =
            localBest.ProjectScore >= ExactTextMatchThreshold &&
            localBest.ProjectCodeConflictPenalty <= 0;

        if (!localBestHasExactProject)
            return true;

        var scoreGap = localBest.FinalScore - second.FinalScore;
        var secondHasProjectMismatchOrConflict =
            second.ProjectScore < ExactTextMatchThreshold ||
            second.ProjectCodeConflictPenalty > 0;

        if (secondHasProjectMismatchOrConflict &&
            scoreGap > ambiguityMargin + ScoreTieEpsilon)
            return false;

        return true;
    }

    private async Task ApplyLlmEquivalenceAdjudicationAsync(
        MatchSource source,
        EvaluatedCandidate best,
        MatchingConfig config,
        LlmCallBudget llmBudget,
        LlmCircuitBreaker llmCircuitBreaker,
        CancellationToken cancellationToken)
    {
        if (_llmEquivalenceAdjudicationService == null ||
            !ShouldRunLlmEquivalenceAdjudication(best, config))
        {
            return;
        }

        if (llmCircuitBreaker.IsOpen)
        {
            best.SelectionSummary = AppendReason(
                best.SelectionSummary,
                "LLM 失败率过高，已触发熔断，灰区行转人工确认");
            return;
        }

        try
        {
            var request = new LlmEquivalenceAdjudicationRequest
            {
                SourceProject = source.Project,
                SourceSpecification = source.Specification,
                CandidateProject = best.Candidate.Project,
                CandidateSpecification = best.Candidate.Specification,
                CandidateAcceptance = best.Candidate.Acceptance,
                CandidateRemark = best.Candidate.Remark,
                CurrentDecision = "manualReview",
                ScoreDetails = CreateScoreDetails(best),
                EvidenceSummary = [.. (best.Evidence?.Summary ?? [])],
                ConflictSummary = [.. (best.Evidence?.Conflicts ?? [])],
                LlmServiceId = config.LlmServiceId
            };
            var execution = await ExecuteLlmCallWithPolicyAsync(
                "AI 等价裁决",
                $"{source.Project}/{source.Specification}",
                config,
                llmBudget,
                llmCircuitBreaker,
                token => _llmEquivalenceAdjudicationService.AdjudicateAsync(request, token),
                cancellationToken);
            var result = execution.Result;

            if (execution.BudgetExhausted)
            {
                best.SelectionSummary = AppendReason(
                    best.SelectionSummary,
                    "LLM 调用已达批次上限，灰区行转人工确认");
            }

            best.LlmEquivalence = result ?? new LlmEquivalenceAdjudicationResult
            {
                Verdict = LlmEquivalenceVerdict.Uncertain,
                ReasonType = LlmEquivalenceReasonType.Uncertain,
                Confidence = 0,
                Reason = execution.BudgetExhausted
                    ? "LLM 调用已达批次上限，已回退为人工确认"
                    : execution.Failed
                    ? "AI 等价裁决失败，已回退为人工确认"
                    : "AI 等价裁决未返回有效结果，已回退为人工确认"
            };
            best.RerankSummary = AppendEquivalenceSummary(best.RerankSummary, best.LlmEquivalence);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI 等价裁决失败，按 uncertain 回退");
            best.LlmEquivalence = new LlmEquivalenceAdjudicationResult
            {
                Verdict = LlmEquivalenceVerdict.Uncertain,
                ReasonType = LlmEquivalenceReasonType.Uncertain,
                Confidence = 0,
                Reason = "AI 等价裁决失败，已回退为人工确认"
            };
            best.RerankSummary = AppendEquivalenceSummary(best.RerankSummary, best.LlmEquivalence);
        }
    }

}
