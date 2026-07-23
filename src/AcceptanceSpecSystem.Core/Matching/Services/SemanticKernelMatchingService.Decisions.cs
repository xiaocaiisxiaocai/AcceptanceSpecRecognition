using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using Microsoft.Extensions.Logging;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public partial class SemanticKernelMatchingService : IMatchingService
{
    private static MatchDecision DetermineDecision(EvaluatedCandidate candidate, bool isAmbiguous, MatchingConfig config)
    {
        // 语义优先模式：LLM Equivalent 具有最高权威，硬冲突规则降级。
        // 但置信度不足时不应盲目自动通过，转人工确认；
        // 型号/料号冲突按更高置信度门槛把关（错填物料是验收场景最危险错误）。
        if (config.EnableLlmSemanticPriority &&
            candidate.LlmEquivalence?.Verdict == LlmEquivalenceVerdict.Equivalent &&
            MeetsEquivalenceConfidenceFloor(candidate, config))
            return MatchDecision.AutoApply;

        // 标准模式：硬冲突绝对门禁（数值/单位/比较符/温度/方向）一律人工，
        // 即使 LLM 误判等价或 Embedding 高分也不放行。
        if (HasHardConflict(candidate.Issues))
            return MatchDecision.ManualReview;

        // 高 Embedding 自动通过：强语义相似 + 无结构化冲突 + 不歧义 + LLM 未判不同 → 自动通过（即使 uncertain）。
        // 硬冲突已在上方拦截；此处再排除型号/料号冲突与未识别(单位/品牌/格式)警告，作为精度闸门。
        if (config.EmbeddingSemanticAutoApplyThreshold > 0 &&
            config.EmbeddingSemanticAutoApplyThreshold <= 1 &&
            candidate.EmbeddingScore >= config.EmbeddingSemanticAutoApplyThreshold - ScoreTieEpsilon &&
            !isAmbiguous &&
            !HasIdentifierConflict(candidate.Issues) &&
            !HasAutoApplyBlockingWarning(candidate.Issues) &&
            candidate.LlmEquivalence?.Verdict != LlmEquivalenceVerdict.Different)
        {
            candidate.SelectionSummary = AppendReason(
                candidate.SelectionSummary,
                $"高 Embedding 语义相似（{candidate.EmbeddingScore:P0}）且无结构化冲突，LLM 未确认，凭语义相似度自动通过，建议优先复查");
            return MatchDecision.AutoApply;
        }

        if (candidate.LlmEquivalence?.Verdict is LlmEquivalenceVerdict.Different or LlmEquivalenceVerdict.Uncertain)
            return MatchDecision.ManualReview;

        if (isAmbiguous)
            return MatchDecision.ManualReview;

        // 标准模式：LLM 判定等价且置信度达标即可放行。
        // 未知单位/品牌/格式 warning 不再先于 LLM 结论拦截——这类行本就是 LLM 擅长的灰区，
        // LLM 已结合上下文确认等价时无需再转人工；型号冲突行要求更高置信度。
        if (candidate.LlmEquivalence?.Verdict == LlmEquivalenceVerdict.Equivalent)
        {
            return MeetsEquivalenceConfidenceFloor(candidate, config)
                ? MatchDecision.AutoApply
                : MatchDecision.ManualReview;
        }

        // 无 LLM 结论（预算耗尽/熔断/未启用）：一律人工确认
        return MatchDecision.ManualReview;
    }

    /// <summary>
    /// LLM Equivalent 结论的置信度门槛：
    /// 常规行按 <see cref="MatchingConfig.LlmEquivalenceMinConfidence"/>；
    /// 存在型号/料号冲突的行按 <see cref="MatchingThresholds.IdentifierConflictEquivalenceMinConfidence"/> 更高门槛。
    /// </summary>
    private static bool MeetsEquivalenceConfidenceFloor(EvaluatedCandidate candidate, MatchingConfig config)
    {
        var confidence = candidate.LlmEquivalence?.Confidence ?? 0;

        if (HasIdentifierConflict(candidate.Issues) &&
            confidence < MatchingThresholds.IdentifierConflictEquivalenceMinConfidence)
        {
            return false;
        }

        return config.LlmEquivalenceMinConfidence <= 0 ||
               confidence >= config.LlmEquivalenceMinConfidence;
    }

    private static bool ShouldRunLlmEquivalenceAdjudication(
        EvaluatedCandidate candidate,
        MatchingConfig config)
    {
        // 语义优先模式隐含需要 LLM：即使 EnableLlmEquivalenceAdjudication 被手动关闭，
        // 语义优先模式也必须调用 LLM，否则扩大的召回候选没有判决依据，全部转人工，
        // 与语义优先的目的（提高覆盖率）完全矛盾。
        if (!config.EnableLlmEquivalenceAdjudication && !config.EnableLlmSemanticPriority)
            return false;

        // 语义优先模式：召回层已降低门槛保留了该候选，LLM 门禁也跟随降低
        if (config.EnableLlmSemanticPriority &&
            candidate.EmbeddingScore >= config.LlmSemanticRecallThreshold)
            return true;

        var llmGateThreshold = Math.Clamp(config.MinScoreThreshold, 0, 1);
        var shouldRunByFinalScore = candidate.FinalScore >= llmGateThreshold;
        var shouldRunByEmbedding = candidate.EmbeddingScore >= llmGateThreshold;
        var shouldRunByCodedProjectRescue = IsCodedProjectSemanticRescueCandidate(candidate);
        // 语义等价救援候选（单位换算/品牌中英文）：项目精确命中但 Embedding 偏低，必须进入 LLM 裁决
        var shouldRunBySemanticEquivalenceRescue = IsSemanticEquivalenceRescueCandidate(
            candidate.EmbeddingScore, candidate.ProjectScore);
        // 未知单位/品牌/格式 warning 或型号冲突：决策依赖 LLM 结论（见 DetermineDecision），强制进入裁决
        var shouldRunByBlockingSignal = HasAutoApplyBlockingWarning(candidate.Issues) ||
                                        HasIdentifierConflict(candidate.Issues);
        // 骨架相似救援候选（数值不同但结构一致，如 3000rpm vs 50r/s）：Embedding 偏低被特别保留，必须进 LLM 裁决
        if (!shouldRunByFinalScore && !shouldRunByEmbedding && !shouldRunByCodedProjectRescue &&
            !shouldRunBySemanticEquivalenceRescue && !shouldRunByBlockingSignal && !candidate.IsSkeletonRescue)
            return false;

        // LLM 等价裁决门槛跟随当前匹配配置的最小得分阈值，
        // 避免页面可见阈值与后端实际触发门槛不一致。
        return true;
    }

    private static bool IsCodedProjectSemanticRescueCandidate(EvaluatedCandidate candidate)
    {
        if (candidate.ProjectScore < ExactTextMatchThreshold ||
            candidate.ProjectCodeConflictPenalty > 0 ||
            candidate.EmbeddingScore < NearTextMatchThreshold - 1e-6)
        {
            return false;
        }

        return TryExtractProjectCode(candidate.Source.Project, out _, out _);
    }

    private static string AppendEquivalenceSummary(
        string? current,
        LlmEquivalenceAdjudicationResult result)
    {
        var summary = $"AI裁决：{GetEquivalenceSummaryText(result)}";
        return string.IsNullOrWhiteSpace(current) ? summary : $"{current}；{summary}";
    }

    private static string BuildAiRerankSelectionSummary(
        IReadOnlyList<EvaluatedCandidate> orderedCandidates,
        EvaluatedCandidate selected,
        LlmCandidateRerankResult rerankResult)
    {
        var selectedRank = orderedCandidates
            .Select((candidate, index) => new { candidate.Candidate.SpecId, Rank = index + 1 })
            .FirstOrDefault(item => item.SpecId == selected.Candidate.SpecId)?
            .Rank ?? 1;

        var prefix = $"AI 从 Top{selectedRank} 改选为当前最佳";
        return string.IsNullOrWhiteSpace(rerankResult.Reason)
            ? prefix
            : $"{prefix}：{rerankResult.Reason}";
    }

    private static string GetEquivalenceSummaryText(LlmEquivalenceAdjudicationResult result)
    {
        var verdictText = result.Verdict switch
        {
            LlmEquivalenceVerdict.Equivalent => "等价",
            LlmEquivalenceVerdict.Different => "不同",
            _ => "不确定"
        };

        var reasonTypeText = result.ReasonType switch
        {
            LlmEquivalenceReasonType.FormatOnly => "仅格式差异",
            LlmEquivalenceReasonType.PunctuationOnly => "仅标点差异",
            LlmEquivalenceReasonType.EquivalentExpression => "等价表达",
            LlmEquivalenceReasonType.SymbolEquivalent => "等价符号",
            LlmEquivalenceReasonType.SemanticDifference => "语义差异",
            LlmEquivalenceReasonType.SymbolConflict => "符号冲突",
            _ => "不确定"
        };

        return string.IsNullOrWhiteSpace(result.Reason)
            ? $"{verdictText}（{reasonTypeText}）"
            : $"{verdictText}（{reasonTypeText}）：{result.Reason}";
    }

    private static bool RequiresManualReview(MatchEvidence? evidence)
    {
        if (evidence == null)
            return false;

        if (evidence.Warnings.Count > 0)
            return true;

        // Conflict 关系（型号/料号冲突）必须阻断确定性自动通过，只能经 LLM 高置信裁决放行
        if (evidence.Identifiers.Any(item =>
                item.Relation is EvidenceRelation.Conflict or EvidenceRelation.Overlap or EvidenceRelation.ParentChild or EvidenceRelation.PossiblyRelated))
            return true;

        if (evidence.Entities.Any(item =>
                item.Relation is EvidenceRelation.Conflict or EvidenceRelation.Overlap or EvidenceRelation.ParentChild or EvidenceRelation.PossiblyRelated))
            return true;

        return false;
    }

    private static bool ShouldKeepCandidate(
        double embeddingScore,
        double projectScore,
        double specificationTextScore,
        double minScoreThreshold,
        MatchingConfig config)
    {
        return embeddingScore >= minScoreThreshold ||
               IsExactProjectRescueCandidate(embeddingScore, projectScore, minScoreThreshold) ||
               IsExactTextRescueCandidate(projectScore, specificationTextScore) ||
               IsSemanticEquivalenceRescueCandidate(embeddingScore, projectScore) ||
               // 语义优先模式：降低召回门槛，让更多候选进入 LLM 视野
               (config.EnableLlmSemanticPriority && embeddingScore >= config.LlmSemanticRecallThreshold);
    }

    /// <summary>
    /// 语义等价救援：项目精确命中但 Embedding 偏低时（单位换算/品牌中英文等），
    /// 保留候选进入 LLM 等价裁决，而不是在召回阶段直接丢弃。
    /// </summary>
    private static bool IsSemanticEquivalenceRescueCandidate(double embeddingScore, double projectScore)
    {
        return projectScore >= ExactTextMatchThreshold &&
               embeddingScore >= SemanticEquivalenceRescueEmbeddingThreshold;
    }

    /// <summary>
    /// 骨架相似救援：规格去数值后的"骨架"完全一致，但 Embedding 落在 [0.50, 召回阈值) 灰带时，
    /// 仍保留候选交由后续裁决。典型场景"电机转速 3000rpm" vs "电机转速 50r/s"——
    /// 单位换算后等价但 Embedding 偏低。仅规格模式下只比骨架；项目+规格模式额外要求项目精确命中，
    /// 避免不同项目间共享通用数值骨架（如"电压#V"）导致召回泛滥。
    /// 骨架计算（Canonicalize+正则）有成本，故仅在常规召回未命中且 Embedding 达到下限时才计算。
    /// </summary>
    private bool IsSkeletonRescueCandidate(
        MatchSource source,
        MatchCandidate candidate,
        double embeddingScore,
        double projectScore,
        MatchingConfig config)
    {
        if (embeddingScore < SkeletonRescueEmbeddingThreshold)
            return false;

        if (config.MatchingMode != MatchingMode.SpecificationOnly &&
            projectScore < ExactTextMatchThreshold)
            return false;

        var sourceSkeleton = BuildCanonicalSpecificationSkeleton(source.Specification);
        if (string.IsNullOrWhiteSpace(sourceSkeleton))
            return false;

        var candidateSkeleton = BuildCanonicalSpecificationSkeleton(candidate.Specification);
        return string.Equals(sourceSkeleton, candidateSkeleton, StringComparison.Ordinal);
    }

    private static bool IsExactProjectRescueCandidate(
        double embeddingScore,
        double projectScore,
        double minScoreThreshold)
    {
        if (projectScore < ExactTextMatchThreshold)
            return false;

        var rescueThreshold = Math.Max(
            MatchingThresholds.MediumConfidenceScore,
            minScoreThreshold - ProjectExactRescueEmbeddingSlack);

        return embeddingScore >= rescueThreshold;
    }

    private static bool IsExactTextRescueCandidate(double projectScore, double specificationTextScore)
    {
        if (specificationTextScore >= ExactTextMatchThreshold)
            return true;

        return projectScore >= ExactTextMatchThreshold &&
               specificationTextScore >= NearTextMatchThreshold;
    }

    private static string GetSourceEmbeddingText(MatchSource source, MatchingConfig config)
    {
        return config.MatchingMode == MatchingMode.SpecificationOnly
            ? source.Specification
            : source.CombinedText;
    }

    private static string GetCandidateEmbeddingText(MatchCandidate candidate, MatchingConfig config)
    {
        return config.MatchingMode == MatchingMode.SpecificationOnly
            ? candidate.Specification
            : candidate.CombinedText;
    }

    private static bool ShouldMarkAsAmbiguous(
        EvaluatedCandidate best,
        EvaluatedCandidate? second,
        double? scoreGap,
        double ambiguityMargin)
    {
        if (second == null || !scoreGap.HasValue)
            return false;

        if (scoreGap.Value > ambiguityMargin + ScoreTieEpsilon)
            return false;

        var bestIsExact =
            best.ProjectScore >= 0.99 &&
            best.SpecificationTextScore >= 0.99 &&
            best.NumericScore >= 0.99;

        var secondIsAlsoExact =
            second.ProjectScore >= 0.99 &&
            second.SpecificationTextScore >= 0.99 &&
            second.NumericScore >= 0.99;

        if (bestIsExact && !secondIsAlsoExact)
            return false;

        return true;
    }

    private static bool NormalizedValueSetsEqual(
        IReadOnlyList<(double BaseValue, string Dimension, string OriginalExpression)> sourceValues,
        IReadOnlyList<(double BaseValue, string Dimension, string OriginalExpression)> candidateValues)
    {
        if (sourceValues.Count == 0 || candidateValues.Count == 0)
            return false;

        var sourceByDim = sourceValues.GroupBy(value => value.Dimension)
            .ToDictionary(group => group.Key, group => group.Select(value => value.BaseValue).OrderBy(value => value).ToList());
        var candidateByDim = candidateValues.GroupBy(value => value.Dimension)
            .ToDictionary(group => group.Key, group => group.Select(value => value.BaseValue).OrderBy(value => value).ToList());

        if (!sourceByDim.Keys.OrderBy(key => key, StringComparer.Ordinal)
                .SequenceEqual(candidateByDim.Keys.OrderBy(key => key, StringComparer.Ordinal), StringComparer.Ordinal))
        {
            return false;
        }

        foreach (var (dimension, sourceList) in sourceByDim)
        {
            var candidateList = candidateByDim[dimension];
            if (sourceList.Count != candidateList.Count)
                return false;

            for (var i = 0; i < sourceList.Count; i++)
            {
                if (IsNumericOutsideEngineeringTolerance(sourceList[i], candidateList[i]))
                    return false;
            }
        }

        return true;
    }

    private string BuildCanonicalSpecificationSkeleton(string specification)
    {
        var canonical = _canonicalizer.Canonicalize(specification);
        canonical = Regex.Replace(canonical, @"-?\d+(?:\.\d+)?(?:e[+-]?\d+)?\[[a-z0-9_]+\]", "#", RegexOptions.IgnoreCase);
        canonical = Regex.Replace(canonical, @"-?\d+(?:\.\d+)?", "#", RegexOptions.IgnoreCase);
        canonical = Regex.Replace(canonical, @"#+", "#");
        canonical = Regex.Replace(canonical, @"\s+", string.Empty);
        return canonical;
    }

    private static bool IsNumericOutsideEngineeringTolerance(double left, double right)
    {
        if (left == 0 && right == 0)
            return false;

        var maxAbs = Math.Max(Math.Abs(left), Math.Abs(right));
        return Math.Abs(left - right) / maxAbs > 1e-3;
    }

    private static List<MatchIssue> BuildCandidateIssues(MatchSource source, EvaluatedCandidate candidate)
    {
        return candidate.Evidence?.Issues.ToList() ?? [];
    }

    private static double ComputeProjectCodeConflictPenalty(string sourceProject, string candidateProject)
    {
        if (!TryExtractProjectCode(sourceProject, out var sourceStem, out var sourceCode) ||
            !TryExtractProjectCode(candidateProject, out var candidateStem, out var candidateCode))
        {
            return 0;
        }

        if (!string.Equals(sourceStem, candidateStem, StringComparison.Ordinal))
            return 0;

        return string.Equals(sourceCode, candidateCode, StringComparison.OrdinalIgnoreCase)
            ? 0
            : ProjectCodeConflictPenaltyScore;
    }

    private static bool TryExtractProjectCode(string? project, out string stem, out string code)
    {
        stem = NormalizeComparableText(project);
        code = string.Empty;

        if (string.IsNullOrWhiteSpace(stem))
            return false;

        var matches = ProjectCodeRegex.Matches(stem);
        if (matches.Count == 0)
            return false;

        var lastMatch = matches[^1];
        code = lastMatch.Groups[1].Value.ToUpperInvariant();
        stem = Regex.Replace(
            $"{stem[..lastMatch.Index]} {stem[(lastMatch.Index + lastMatch.Length)..]}",
            @"\s+",
            " ")
            .Trim();

        return !string.IsNullOrWhiteSpace(stem);
    }

    private static string NormalizeComparableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value
            .Replace("\u00A0", " ", StringComparison.Ordinal)
            .Replace("\u200B", string.Empty, StringComparison.Ordinal)
            .Replace("\uFEFF", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"\s+", " ");
        normalized = normalized.Replace("（", "(").Replace("）", ")");
        return normalized;
    }

    private static string NormalizeSpecificationComparableText(string? value)
    {
        var normalized = NormalizeComparableText(value);
        if (string.IsNullOrEmpty(normalized))
            return normalized;

        // 中英文资料常混用“到 / 至 / to”表示相同数值区间。
        // 仅在连接符左侧为数值或单位字符、右侧为数字时归一化，避免误改普通英文单词。
        normalized = Regex.Replace(
            normalized,
            @"(?<=[\dA-Za-z%℃°μµ])\s*(?:到|至|to)\s*(?=\d)",
            "~",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Regex.Replace(
            normalized,
            @"(?<=[\p{IsCJKUnifiedIdeographs}])\s+(?=\d)",
            string.Empty,
            RegexOptions.CultureInvariant);
    }

    private static void EnsureEmbeddingBatchPayload(IReadOnlyList<float[]> embeddings, int expectedCount, string targetName)
    {
        if (embeddings.Count != expectedCount)
        {
            throw new AiServiceUnavailableException(
                $"Embedding 服务返回数量与请求不一致：{targetName}请求 {expectedCount} 个，实际返回 {embeddings.Count} 个");
        }

        if (embeddings.Any(embedding => embedding == null || embedding.Length == 0))
        {
            throw new AiServiceUnavailableException($"{targetName} Embedding 结果为空");
        }
    }

    private static bool HasText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// 判断候选的问题列表中是否存在硬冲突（数值/单位、比较符、温度跨温标、方向/极性反义）。
    /// 硬冲突一律强制人工，无视 Embedding 高分。
    /// </summary>
    private static bool HasHardConflict(IReadOnlyList<MatchIssue>? issues)
    {
        return issues != null && issues.Any(issue =>
            string.Equals(issue.Severity, "hard_conflict", StringComparison.Ordinal));
    }

    private static bool HasAutoApplyBlockingWarning(IReadOnlyList<MatchIssue>? issues)
    {
        return issues != null && issues.Any(issue =>
            string.Equals(issue.Severity, "warning", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(issue.Code, "unknown_unit_token", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(issue.Code, "unknown_brand_token", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(issue.Code, "unsupported_format_token", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// 判断候选的问题列表中是否存在型号/料号冲突。
    /// 此类行的 LLM Equivalent 结论需满足更高置信度门槛才可自动通过。
    /// </summary>
    private static bool HasIdentifierConflict(IReadOnlyList<MatchIssue>? issues)
    {
        return issues != null && issues.Any(issue =>
            string.Equals(issue.Code, "identifier_conflict", StringComparison.OrdinalIgnoreCase));
    }
}
