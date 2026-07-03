using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using Microsoft.Extensions.Logging;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public partial class SemanticKernelMatchingService : IMatchingService
{
    private List<EvaluatedCandidate> EvaluateCandidates(
        MatchSource source,
        float[] sourceEmbedding,
        List<MatchCandidate> candidateList,
        MatchingConfig config)
    {
        var evaluations = new List<EvaluatedCandidate>();
        foreach (var candidate in candidateList)
        {
            var embedding = candidate.Embedding ?? Array.Empty<float>();
            var embeddingScore = _embeddingService.ComputeSimilarity(sourceEmbedding, embedding);
            var projectScore = ComputeProjectScore(source.Project, candidate.Project);
            var specificationTextScore = ComputeSpecificationTextScore(
                source.Specification,
                candidate.Specification);
            var projectCodeConflictPenalty = config.MatchingMode == MatchingMode.SpecificationOnly
                ? 0
                : ComputeProjectCodeConflictPenalty(
                    source.Project,
                    candidate.Project);

            var shouldKeep = ShouldKeepCandidate(
                embeddingScore,
                projectScore,
                specificationTextScore,
                config.MinScoreThreshold,
                config);
            var isSkeletonRescue = false;
            if (!shouldKeep)
            {
                isSkeletonRescue = IsSkeletonRescueCandidate(source, candidate, embeddingScore, projectScore, config);
                if (!isSkeletonRescue)
                {
                    continue;
                }
            }

            evaluations.Add(new EvaluatedCandidate
            {
                Source = source,
                Candidate = candidate,
                EmbeddingScore = embeddingScore,
                ProjectScore = projectScore,
                SpecificationTextScore = specificationTextScore,
                ProjectCodeConflictPenalty = projectCodeConflictPenalty,
                IsSkeletonRescue = isSkeletonRescue,
                MatchBasis = config.MatchingMode == MatchingMode.SpecificationOnly
                    ? MatchBasis.Specification
                    : MatchBasis.ProjectSpecification,
                FinalScore = embeddingScore
            });
        }

        return evaluations;
    }

    private async Task<MatchResult?> SelectBestCandidateAsync(
        MatchSource source,
        List<EvaluatedCandidate> eligibleCandidates,
        MatchingConfig config,
        LlmCallBudget llmBudget,
        LlmCircuitBreaker llmCircuitBreaker,
        CancellationToken cancellationToken)
    {
        var recallTopK = Math.Clamp(config.RecallTopK, 1, MatchingThresholds.MaxRecallTopK);
        var recalled = OrderByEmbedding(eligibleCandidates)
            .Take(recallTopK)
            .ToList();

        if (recalled.Count == 0)
            return null;

        foreach (var candidate in recalled)
        {
            candidate.Evidence = _evidenceBuilder.Build(source, candidate.Candidate);
            candidate.NumericScore = ComputeNumericScore(source, candidate);
            candidate.Issues = BuildCandidateIssues(source, candidate);
            candidate.FinalScore = ComputeFinalScore(candidate);
            candidate.RerankSummary = BuildRerankSummary(candidate);
            candidate.SelectionMode = MatchSelectionMode.EmbeddingTop1;
        }

        var locallyOrdered = OrderByFinal(recalled).ToList();
        var best = await SelectCurrentBestCandidateAsync(
            source,
            locallyOrdered,
            config,
            llmBudget,
            llmCircuitBreaker,
            cancellationToken);
        var ordered = ReorderSelectedCandidateFirst(locallyOrdered, best);

        var second = ordered.Count > 1 ? ordered[1] : null;
        double? scoreGap = second == null ? null : best.FinalScore - second.FinalScore;
        var isAmbiguous = ShouldMarkAsAmbiguous(best, second, scoreGap, config.AmbiguityMargin);

        // 决策优先级（确定性优先，LLM 仅作灰区兜底）：
        // 1. 有 hard_conflict → 标准模式强制人工；语义优先模式仍调 LLM，由 DetermineDecision 决定放行
        // 2. 无冲突 + Embedding≥高置信阈值 + 不歧义 → 确定性 AutoApply，不调 LLM
        // 3. 其余灰区且预算未耗尽 → LLM 等价裁决兜底
        // 4. 灰区但预算耗尽 → 维持人工
        var hasHardConflict = HasHardConflict(best.Issues);
        if (hasHardConflict && !config.EnableLlmSemanticPriority)
        {
            best.SelectionSummary = AppendReason(best.SelectionSummary, "检测到硬冲突（数值/单位/比较符/温度/方向），强制人工确认");
        }
        else if (!hasHardConflict && CanDeterministicAutoApply(best, config, isAmbiguous))
        {
            best.LlmEquivalence ??= CreateDeterministicAutoApplyEquivalence(best, config);
            best.SelectionSummary = AppendReason(best.SelectionSummary, "无结构化冲突且 Embedding 达到高置信，确定性自动通过");
        }
        else
        {
            if (hasHardConflict && config.EnableLlmSemanticPriority)
            {
                best.SelectionSummary = AppendReason(best.SelectionSummary, "检测到硬冲突，语义优先模式下交由 LLM 裁决");
            }
            await ApplyLlmEquivalenceAdjudicationAsync(
                source,
                best,
                config,
                llmBudget,
                llmCircuitBreaker,
                cancellationToken);
        }

        return BuildMatchResult(
            best,
            recalled.Count,
            isAmbiguous,
            scoreGap,
            config.MinScoreThreshold,
            config.HighConfidenceThreshold,
            config,
            orderedCandidates: ordered);
    }

    /// <summary>
    /// 确定性自动通过判定：在没有任何硬冲突的前提下，
    /// Embedding 达到高置信阈值且不歧义即可自动通过，无需 LLM。
    /// 这是把 LLM 移出匹配热路径的核心：高度相似且无结构冲突的行不再逐一打 LLM。
    /// </summary>
    private static bool CanDeterministicAutoApply(
        EvaluatedCandidate candidate,
        MatchingConfig config,
        bool isAmbiguous)
    {
        if (!config.EnableDeterministicAutoApply || isAmbiguous)
            return false;

        // 证据层若标注了需人工关注的警告/重叠关系，不走确定性自动通过
        if (RequiresManualReview(candidate.Evidence))
            return false;

        var highConfidence = MatchingThresholds.NormalizeHighConfidenceThreshold(config.HighConfidenceThreshold);
        return candidate.EmbeddingScore >= highConfidence - ScoreTieEpsilon &&
               candidate.FinalScore >= highConfidence - ScoreTieEpsilon;
    }

    private static LlmEquivalenceAdjudicationResult CreateDeterministicAutoApplyEquivalence(
        EvaluatedCandidate candidate,
        MatchingConfig config)
    {
        return new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = candidate.EmbeddingScore,
            Reason = "无数值/单位/比较符/方向冲突，且语义相似度达到高置信阈值，确定性判定等价"
        };
    }

    private static string AppendReason(string? current, string reason)
    {
        return string.IsNullOrWhiteSpace(current) ? reason : $"{current}；{reason}";
    }

}
