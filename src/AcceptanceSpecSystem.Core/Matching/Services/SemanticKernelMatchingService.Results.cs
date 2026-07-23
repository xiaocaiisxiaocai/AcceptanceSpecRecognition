using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using Microsoft.Extensions.Logging;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public partial class SemanticKernelMatchingService : IMatchingService
{
    private static IEnumerable<EvaluatedCandidate> OrderByEmbedding(IEnumerable<EvaluatedCandidate> candidates)
    {
        return candidates
            .OrderByDescending(c => c.EmbeddingScore)
            .ThenByDescending(c => c.ProjectScore)
            .ThenByDescending(c => c.SpecificationTextScore)
            .ThenByDescending(c => HasText(c.Candidate.Acceptance))
            .ThenByDescending(c => HasText(c.Candidate.Remark))
            .ThenByDescending(c => c.Candidate.SpecId);
    }

    private static IEnumerable<EvaluatedCandidate> OrderByFinal(IEnumerable<EvaluatedCandidate> candidates)
    {
        return candidates
            .OrderByDescending(c => c.FinalScore)
            .ThenByDescending(c => c.EmbeddingScore)
            .ThenByDescending(c => c.ProjectScore)
            .ThenByDescending(c => c.SpecificationTextScore)
            .ThenByDescending(c => HasText(c.Candidate.Acceptance))
            .ThenByDescending(c => HasText(c.Candidate.Remark))
            .ThenByDescending(c => c.Candidate.SpecId);
    }

    private static MatchResult BuildMatchResult(
        EvaluatedCandidate candidate,
        int recalledCandidateCount,
        bool isAmbiguous,
        double? scoreGap,
        double minScoreThreshold,
        double highConfidenceThreshold,
        MatchingConfig config,
        IReadOnlyList<EvaluatedCandidate> orderedCandidates)
    {
        var scoreDetails = CreateScoreDetails(candidate);

        return new MatchResult
        {
            SourceText = candidate.Source.CombinedText,
            MatchedText = candidate.Candidate.CombinedText,
            MatchedSpecId = candidate.Candidate.SpecId,
            MatchedProject = candidate.Candidate.Project,
            MatchedSpecification = candidate.Candidate.Specification,
            MatchedAcceptance = candidate.Candidate.Acceptance,
            MatchedRemark = candidate.Candidate.Remark,
            Score = candidate.FinalScore,
            EmbeddingScore = candidate.EmbeddingScore,
            ScoreDetails = scoreDetails,
            Evidence = candidate.Evidence ?? new MatchEvidence(),
            Issues = candidate.Issues ?? [],
            RecalledCandidateCount = recalledCandidateCount,
            IsAmbiguous = isAmbiguous,
            ScoreGap = scoreGap,
            RerankSummary = candidate.RerankSummary,
            SelectionMode = candidate.SelectionMode,
            SelectionSummary = candidate.SelectionSummary,
            MatchBasis = candidate.MatchBasis,
            Decision = DetermineDecision(candidate, isAmbiguous, config),
            MinScoreThreshold = minScoreThreshold,
            HighConfidenceThreshold = MatchingThresholds.NormalizeHighConfidenceThreshold(highConfidenceThreshold),
            LlmEquivalenceMinConfidence = config.LlmEquivalenceMinConfidence,
            TopCandidates = BuildTopCandidates(orderedCandidates),
            LlmEquivalence = candidate.LlmEquivalence
        };
    }

    private static MatchResult CreateEmptyResult(MatchSource source)
    {
        return new MatchResult
        {
            SourceText = source.CombinedText,
            Score = 0,
            EmbeddingScore = 0,
            Evidence = new MatchEvidence(),
            Issues = [],
            Decision = MatchDecision.ManualReview,
            RecalledCandidateCount = 0,
            IsAmbiguous = false
        };
    }

    private static Dictionary<string, double> CreateScoreDetails(EvaluatedCandidate candidate)
    {
        return new Dictionary<string, double>
        {
            ["Embedding"] = candidate.EmbeddingScore,
            ["Final"] = candidate.FinalScore,
            ["ProjectMatch"] = candidate.ProjectScore,
            ["SpecificationText"] = candidate.SpecificationTextScore,
            ["NumberUnit"] = candidate.NumericScore,
            ["ProjectCodePenalty"] = candidate.ProjectCodeConflictPenalty
        };
    }

    private static List<MatchCandidateSnapshot> BuildTopCandidates(IReadOnlyList<EvaluatedCandidate> orderedCandidates)
    {
        return orderedCandidates
            .Take(TopCandidateLimit)
            .Select((candidate, index) => new MatchCandidateSnapshot
            {
                Rank = index + 1,
                SpecId = candidate.Candidate.SpecId,
                Project = candidate.Candidate.Project,
                Specification = candidate.Candidate.Specification,
                Acceptance = candidate.Candidate.Acceptance,
                Remark = candidate.Candidate.Remark,
                Score = candidate.FinalScore,
                EmbeddingScore = candidate.EmbeddingScore,
                ScoreDetails = CreateScoreDetails(candidate),
                Evidence = candidate.Evidence ?? new MatchEvidence(),
                Issues = candidate.Issues ?? [],
                RerankSummary = candidate.RerankSummary,
                SelectionMode = candidate.SelectionMode,
                SelectionSummary = candidate.SelectionSummary,
                MatchBasis = candidate.MatchBasis,
                LlmEquivalence = candidate.LlmEquivalence
            })
            .ToList();
    }

    private static List<EvaluatedCandidate> ReorderSelectedCandidateFirst(
        IReadOnlyList<EvaluatedCandidate> orderedCandidates,
        EvaluatedCandidate selected)
    {
        var reordered = orderedCandidates.ToList();
        var selectedIndex = reordered.FindIndex(candidate => candidate.Candidate.SpecId == selected.Candidate.SpecId);
        if (selectedIndex <= 0)
        {
            return reordered;
        }

        reordered.RemoveAt(selectedIndex);
        reordered.Insert(0, selected);
        return reordered;
    }

    private static double ComputeFinalScore(EvaluatedCandidate candidate)
    {
        var finalScore = candidate.MatchBasis == MatchBasis.Specification
            ? candidate.EmbeddingScore * 0.55 +
              candidate.SpecificationTextScore * 0.30 +
              candidate.NumericScore * 0.15
            : candidate.EmbeddingScore * 0.55 +
              candidate.ProjectScore * 0.15 +
              candidate.SpecificationTextScore * 0.15 +
              candidate.NumericScore * 0.15 -
              candidate.ProjectCodeConflictPenalty;

        return Math.Clamp(finalScore, 0, 1);
    }

    private static double ComputeProjectScore(string sourceProject, string candidateProject)
    {
        var source = NormalizeComparableText(sourceProject);
        var candidate = NormalizeComparableText(candidateProject);

        if (string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(candidate))
            return 1.0;

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(candidate))
            return 0;

        if (source == candidate)
            return 1.0;

        if (source.Contains(candidate, StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains(source, StringComparison.OrdinalIgnoreCase))
            return 0.85;

        return 0;
    }

    /// <summary>
    /// 数值/单位维度得分（展示为 NumberUnit）：
    /// 1.0 —— 规格文本一致，或双侧可归一数值集合在工程容差内等价，或双侧均无任何数字；
    /// 0.0 —— 双侧均有可归一数值但集合不等价（数值/量纲冲突）；
    /// 0.5 —— 数字无法归一比较（裸数字/未知单位等），保持中性。
    /// </summary>
    private double ComputeNumericScore(MatchSource source, EvaluatedCandidate candidate)
    {
        var sourceText = NormalizeSpecificationComparableText(source.Specification);
        var candidateText = NormalizeSpecificationComparableText(candidate.Candidate.Specification);

        if (string.IsNullOrWhiteSpace(sourceText) && string.IsNullOrWhiteSpace(candidateText))
            return 1.0;

        if (!string.IsNullOrWhiteSpace(sourceText) && sourceText == candidateText)
            return 1.0;

        var sourceValues = _canonicalizer.ExtractNormalizedValues(source.Specification);
        var candidateValues = _canonicalizer.ExtractNormalizedValues(candidate.Candidate.Specification);
        if (sourceValues.Count > 0 && candidateValues.Count > 0)
            return NormalizedValueSetsEqual(sourceValues, candidateValues) ? 1.0 : 0.0;

        // 双侧文本均不含数字：数值维度无差异可言，不应拖累综合分
        if (!ContainsDigit(sourceText) && !ContainsDigit(candidateText))
            return 1.0;

        return 0.5;
    }

    private static bool ContainsDigit(string text)
    {
        foreach (var ch in text)
        {
            if (char.IsDigit(ch))
                return true;
        }

        return false;
    }

    private static double ComputeSpecificationTextScore(string sourceSpecification, string candidateSpecification)
    {
        var source = NormalizeSpecificationComparableText(sourceSpecification);
        var candidate = NormalizeSpecificationComparableText(candidateSpecification);

        if (string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(candidate))
            return 1.0;

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(candidate))
            return 0;

        if (source == candidate)
            return 1.0;

        if (source.Contains(candidate, StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains(source, StringComparison.OrdinalIgnoreCase))
            return 0.88;

        return 0;
    }

    private static string BuildRerankSummary(EvaluatedCandidate candidate)
    {
        var reasons = new List<string>();

        if (candidate.Evidence?.Summary.Count > 0)
            reasons.AddRange(candidate.Evidence.Summary);

        if (candidate.ProjectCodeConflictPenalty > 0)
            reasons.Add("项目编号冲突已降权");

        if (candidate.ProjectScore >= 0.99)
            reasons.Add("项目一致");
        else if (candidate.ProjectScore >= 0.75)
            reasons.Add("项目接近");

        if (candidate.SpecificationTextScore >= 0.99)
            reasons.Add("规格文本一致");
        else if (candidate.SpecificationTextScore >= 0.75)
            reasons.Add("规格文本接近");

        if (reasons.Count == 0)
            reasons.Add("主要依据Embedding排序");

        return string.Join("；", reasons);
    }

}
