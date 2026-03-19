using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// 基于 Semantic Kernel Embedding 的匹配服务
/// Embedding 不可用时直接抛出异常，由上层返回明确错误
/// </summary>
public class SemanticKernelMatchingService : IMatchingService
{
    private const double ScoreTieEpsilon = 1e-9;
    private const int TopCandidateLimit = 3;
    private static readonly Regex NumericTokenRegex = new(
        @"[<>≤≥]?\s*\d+(?:\.\d+)?(?:\s*[x×~～\-]\s*\d+(?:\.\d+)?)*(?:\s*(?:mm|cm|m|kg|g|inch|in|pcs|台|%|℃|°|kpa|mpa|nm|w|kw|v|a|hz|s|min|hr|hrs|小时|秒|ms))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex KeywordTokenRegex = new(
        @"[A-Za-z]{2,}[A-Za-z0-9\-]*|[\u4e00-\u9fff]{2,}",
        RegexOptions.Compiled);
    private static readonly HashSet<string> KeywordStopWords =
    [
        "the", "and", "for", "with", "from", "into", "onto", "shall", "must", "need",
        "项目", "规格", "要求", "技术", "参数", "内容", "方式", "备注", "进行", "支持", "具备", "根据"
    ];
    private static readonly (string Left, string Right)[] ConflictPairs =
    [
        ("投板", "收板"),
        ("放板", "收板"),
        ("上料", "下料"),
        ("进板", "出板"),
        ("入口", "出口"),
        ("loading", "unloading"),
        ("loader", "unloader")
    ];

    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<SemanticKernelMatchingService> _logger;

    public SemanticKernelMatchingService(
        IEmbeddingService embeddingService,
        ILogger<SemanticKernelMatchingService> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task<List<MatchResult>> FindMatchesAsync(
        MatchSource source,
        IEnumerable<MatchCandidate> candidates,
        MatchingConfig? config = null)
    {
        config ??= new MatchingConfig();
        var candidateList = candidates.ToList();

        if (string.IsNullOrWhiteSpace(source?.CombinedText) || candidateList.Count == 0)
        {
            return [];
        }

        var batchResult = await BatchMatchAsync([source], candidateList, config);
        return batchResult.Results
            .Where(r => r.MatchedSpecId.HasValue)
            .ToList();
    }

    /// <summary>
    /// 批量匹配：一次性生成所有 Embedding 后计算相似度，大幅减少 API 调用次数
    /// 注意：不会静默降级到文本相似度，Embedding 不可用时直接抛出异常
    /// </summary>
    public async Task<BatchMatchResult> BatchMatchAsync(
        IEnumerable<MatchSource> sources,
        IEnumerable<MatchCandidate> candidates,
        MatchingConfig? config = null)
    {
        config ??= new MatchingConfig();
        var sourceList = sources.ToList();
        var candidateList = candidates.ToList();

        if (sourceList.Count == 0)
            return new BatchMatchResult();

        return await BatchMatchByEmbeddingAsync(sourceList, candidateList, config);
    }

    /// <summary>
    /// 批量 Embedding 匹配：
    /// 步骤1 - 一次性批量生成所有源文本 Embedding
    /// 步骤2 - 一次性批量生成所有缺失候选 Embedding（复用已有缓存）
    /// 步骤3 - 对每条源文本按配置选择单阶段或多阶段结果
    /// </summary>
    private async Task<BatchMatchResult> BatchMatchByEmbeddingAsync(
        List<MatchSource> sourceList,
        List<MatchCandidate> candidateList,
        MatchingConfig config)
    {
        List<float[]> sourceEmbeddings;
        try
        {
            sourceEmbeddings = await _embeddingService.GenerateEmbeddingsAsync(
                sourceList.Select(s => s.CombinedText),
                config.EmbeddingServiceId);
            _logger.LogInformation("批量生成 {Count} 个源文本 Embedding 完成", sourceList.Count);
        }
        catch (AiServiceUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "批量生成源文本 Embedding 失败");
            throw new AiServiceUnavailableException("Embedding 服务不可用", innerException: ex);
        }

        await EnsureCandidateEmbeddingsAsync(candidateList, config);

        var result = new BatchMatchResult();
        for (var s = 0; s < sourceList.Count; s++)
        {
            var source = sourceList[s];
            var sourceEmbedding = s < sourceEmbeddings.Count ? sourceEmbeddings[s] : Array.Empty<float>();
            var eligibleCandidates = EvaluateCandidates(source, sourceEmbedding, candidateList, config);

            var match = config.MatchingStrategy == MatchingStrategy.MultiStage
                ? SelectBestByMultiStage(source, eligibleCandidates, config)
                : SelectBestBySingleStage(source, eligibleCandidates);

            result.Results.Add(match ?? CreateEmptyResult(source, config.MatchingStrategy));
        }

        return result;
    }

    private async Task EnsureCandidateEmbeddingsAsync(List<MatchCandidate> candidateList, MatchingConfig config)
    {
        var missingIndices = new List<int>();
        for (var i = 0; i < candidateList.Count; i++)
        {
            if (candidateList[i].Embedding == null)
                missingIndices.Add(i);
        }

        if (missingIndices.Count == 0)
        {
            _logger.LogDebug("全部 {Count} 个候选项 Embedding 已缓存，跳过远程调用", candidateList.Count);
            return;
        }

        var missingTexts = missingIndices.Select(i => candidateList[i].CombinedText).ToList();
        List<float[]> newEmbeddings;
        try
        {
            newEmbeddings = await _embeddingService.GenerateEmbeddingsAsync(missingTexts, config.EmbeddingServiceId);
        }
        catch (AiServiceUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "批量生成候选 Embedding 失败");
            throw new AiServiceUnavailableException("Embedding 服务不可用", innerException: ex);
        }

        for (var j = 0; j < missingIndices.Count && j < newEmbeddings.Count; j++)
        {
            candidateList[missingIndices[j]].Embedding = newEmbeddings[j];
        }

        _logger.LogInformation("生成 {Count}/{Total} 个候选项 Embedding（复用 {Cached} 个已缓存）",
            missingIndices.Count, candidateList.Count, candidateList.Count - missingIndices.Count);
    }

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

            if (!ShouldKeepCandidate(
                    embeddingScore,
                    config.MinScoreThreshold,
                    projectScore,
                    specificationTextScore))
            {
                continue;
            }

            evaluations.Add(new EvaluatedCandidate
            {
                Source = source,
                Candidate = candidate,
                EmbeddingScore = embeddingScore,
                ProjectScore = projectScore,
                SpecificationTextScore = specificationTextScore,
                FinalScore = embeddingScore
            });
        }

        return evaluations;
    }

    private MatchResult? SelectBestBySingleStage(MatchSource source, List<EvaluatedCandidate> eligibleCandidates)
    {
        var ordered = OrderByEmbedding(eligibleCandidates).ToList();
        var best = ordered.FirstOrDefault();
        return best == null
            ? null
            : BuildMatchResult(
                best,
                MatchingStrategy.SingleStage,
                eligibleCandidates.Count,
                isAmbiguous: false,
                scoreGap: null,
                orderedCandidates: ordered);
    }

    private MatchResult? SelectBestByMultiStage(
        MatchSource source,
        List<EvaluatedCandidate> eligibleCandidates,
        MatchingConfig config)
    {
        var recallTopK = Math.Clamp(config.RecallTopK, 1, 20);
        var recalled = OrderByEmbedding(eligibleCandidates)
            .Take(recallTopK)
            .ToList();

        if (recalled.Count == 0)
            return null;

        foreach (var candidate in recalled)
        {
            candidate.NumericScore = ComputeNumericScore(source.Specification, candidate.Candidate.Specification);
            candidate.KeywordScore = ComputeKeywordScore(source.Specification, candidate.Candidate.Specification);
            candidate.ConflictPenalty = ComputeConflictPenalty(source, candidate.Candidate);
            candidate.FinalScore = ComputeFinalScore(candidate);
            candidate.RerankSummary = BuildRerankSummary(candidate);
        }

        var ordered = OrderByFinal(recalled).ToList();
        var best = ordered[0];
        var second = ordered.Count > 1 ? ordered[1] : null;
        double? scoreGap = second == null ? null : best.FinalScore - second.FinalScore;
        var isAmbiguous = ShouldMarkAsAmbiguous(best, second, scoreGap, config.AmbiguityMargin);

        return BuildMatchResult(
            best,
            MatchingStrategy.MultiStage,
            recalled.Count,
            isAmbiguous,
            scoreGap,
            orderedCandidates: ordered);
    }

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
        MatchingStrategy strategy,
        int recalledCandidateCount,
        bool isAmbiguous,
        double? scoreGap,
        IReadOnlyList<EvaluatedCandidate> orderedCandidates)
    {
        var scoreDetails = CreateScoreDetails(candidate, strategy);

        return new MatchResult
        {
            SourceText = candidate.Source.CombinedText,
            MatchedText = candidate.Candidate.CombinedText,
            MatchedSpecId = candidate.Candidate.SpecId,
            MatchedProject = candidate.Candidate.Project,
            MatchedSpecification = candidate.Candidate.Specification,
            MatchedAcceptance = candidate.Candidate.Acceptance,
            MatchedRemark = candidate.Candidate.Remark,
            Score = strategy == MatchingStrategy.MultiStage ? candidate.FinalScore : candidate.EmbeddingScore,
            EmbeddingScore = candidate.EmbeddingScore,
            ScoreDetails = scoreDetails,
            MatchingStrategy = strategy,
            RecalledCandidateCount = recalledCandidateCount,
            IsAmbiguous = isAmbiguous,
            ScoreGap = scoreGap,
            RerankSummary = strategy == MatchingStrategy.MultiStage ? candidate.RerankSummary : null,
            TopCandidates = BuildTopCandidates(orderedCandidates, strategy)
        };
    }

    private static MatchResult CreateEmptyResult(MatchSource source, MatchingStrategy strategy)
    {
        return new MatchResult
        {
            SourceText = source.CombinedText,
            Score = 0,
            EmbeddingScore = 0,
            MatchingStrategy = strategy,
            RecalledCandidateCount = 0,
            IsAmbiguous = false
        };
    }

    private static Dictionary<string, double> CreateScoreDetails(
        EvaluatedCandidate candidate,
        MatchingStrategy strategy)
    {
        var scoreDetails = new Dictionary<string, double>
        {
            ["Embedding"] = candidate.EmbeddingScore
        };

        if (strategy == MatchingStrategy.MultiStage)
        {
            scoreDetails["Final"] = candidate.FinalScore;
            scoreDetails["ProjectMatch"] = candidate.ProjectScore;
            scoreDetails["SpecificationText"] = candidate.SpecificationTextScore;
            scoreDetails["NumberUnit"] = candidate.NumericScore;
            scoreDetails["KeywordOverlap"] = candidate.KeywordScore;
            scoreDetails["ConflictPenalty"] = candidate.ConflictPenalty;
        }

        return scoreDetails;
    }

    private static List<MatchCandidateSnapshot> BuildTopCandidates(
        IReadOnlyList<EvaluatedCandidate> orderedCandidates,
        MatchingStrategy strategy)
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
                Score = strategy == MatchingStrategy.MultiStage ? candidate.FinalScore : candidate.EmbeddingScore,
                EmbeddingScore = candidate.EmbeddingScore,
                ScoreDetails = CreateScoreDetails(candidate, strategy),
                RerankSummary = strategy == MatchingStrategy.MultiStage ? candidate.RerankSummary : null
            })
            .ToList();
    }

    private static double ComputeFinalScore(EvaluatedCandidate candidate)
    {
        var finalScore =
            candidate.EmbeddingScore * 0.55 +
            candidate.ProjectScore * 0.15 +
            candidate.SpecificationTextScore * 0.15 +
            candidate.NumericScore * 0.10 +
            candidate.KeywordScore * 0.05 -
            candidate.ConflictPenalty * 0.15;

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

        var sourceTokens = ExtractKeywordTokens(sourceProject);
        var candidateTokens = ExtractKeywordTokens(candidateProject);
        return ComputeOverlapRatio(sourceTokens, candidateTokens);
    }

    private static double ComputeNumericScore(string sourceSpecification, string candidateSpecification)
    {
        var source = NormalizeComparableText(sourceSpecification);
        var candidate = NormalizeComparableText(candidateSpecification);

        if (!string.IsNullOrWhiteSpace(source) && source == candidate)
            return 1.0;

        var sourceTokens = ExtractNumericTokens(sourceSpecification);
        var candidateTokens = ExtractNumericTokens(candidateSpecification);

        if (sourceTokens.Count == 0 && candidateTokens.Count == 0)
            return 0.5;

        if (sourceTokens.Count == 0)
            return 0.5;

        if (candidateTokens.Count == 0)
            return 0;

        return ComputeOverlapRatio(sourceTokens, candidateTokens);
    }

    private static double ComputeSpecificationTextScore(string sourceSpecification, string candidateSpecification)
    {
        var source = NormalizeComparableText(sourceSpecification);
        var candidate = NormalizeComparableText(candidateSpecification);

        if (string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(candidate))
            return 1.0;

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(candidate))
            return 0;

        if (source == candidate)
            return 1.0;

        if (source.Contains(candidate, StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains(source, StringComparison.OrdinalIgnoreCase))
            return 0.88;

        var sourceTokens = ExtractKeywordTokens(sourceSpecification);
        var candidateTokens = ExtractKeywordTokens(candidateSpecification);
        return ComputeOverlapRatio(sourceTokens, candidateTokens);
    }

    private static double ComputeKeywordScore(string sourceSpecification, string candidateSpecification)
    {
        var sourceTokens = ExtractKeywordTokens(sourceSpecification);
        var candidateTokens = ExtractKeywordTokens(candidateSpecification);

        if (sourceTokens.Count == 0 && candidateTokens.Count == 0)
            return 0.5;

        if (sourceTokens.Count == 0 || candidateTokens.Count == 0)
            return 0;

        return ComputeOverlapRatio(sourceTokens, candidateTokens);
    }

    private static double ComputeConflictPenalty(MatchSource source, MatchCandidate candidate)
    {
        var sourceText = NormalizeComparableText($"{source.Project} {source.Specification}");
        var candidateText = NormalizeComparableText($"{candidate.Project} {candidate.Specification}");

        foreach (var (left, right) in ConflictPairs)
        {
            var sourceHasLeft = sourceText.Contains(left, StringComparison.OrdinalIgnoreCase);
            var sourceHasRight = sourceText.Contains(right, StringComparison.OrdinalIgnoreCase);
            var candidateHasLeft = candidateText.Contains(left, StringComparison.OrdinalIgnoreCase);
            var candidateHasRight = candidateText.Contains(right, StringComparison.OrdinalIgnoreCase);

            if (sourceHasLeft && !sourceHasRight && candidateHasRight && !candidateHasLeft)
                return 1.0;

            if (sourceHasRight && !sourceHasLeft && candidateHasLeft && !candidateHasRight)
                return 1.0;
        }

        return 0;
    }

    private static string BuildRerankSummary(EvaluatedCandidate candidate)
    {
        var reasons = new List<string>();

        if (candidate.ProjectScore >= 0.99)
            reasons.Add("项目一致");
        else if (candidate.ProjectScore >= 0.75)
            reasons.Add("项目接近");

        if (candidate.SpecificationTextScore >= 0.99)
            reasons.Add("规格文本一致");
        else if (candidate.SpecificationTextScore >= 0.75)
            reasons.Add("规格文本接近");

        if (candidate.NumericScore >= 0.99)
            reasons.Add("数值单位一致");
        else if (candidate.NumericScore >= 0.60)
            reasons.Add("数值单位部分匹配");

        if (candidate.KeywordScore >= 0.60)
            reasons.Add("关键词重合高");

        if (candidate.ConflictPenalty > 0)
            reasons.Add("存在冲突词已降权");

        if (reasons.Count == 0)
            reasons.Add("主要依据Embedding排序");

        return string.Join("；", reasons);
    }

    private static bool ShouldKeepCandidate(
        double embeddingScore,
        double minScoreThreshold,
        double projectScore,
        double specificationTextScore)
    {
        if (embeddingScore >= minScoreThreshold)
            return true;

        if (projectScore >= 0.99 && specificationTextScore >= 0.99)
            return true;

        var relaxedThreshold = Math.Max(0.35, minScoreThreshold - 0.08);
        return embeddingScore >= relaxedThreshold &&
               projectScore >= 0.99 &&
               specificationTextScore >= 0.88;
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

    private static HashSet<string> ExtractNumericTokens(string value)
    {
        var matches = NumericTokenRegex.Matches(value ?? string.Empty);
        return matches
            .Select(m => NormalizeComparableText(m.Value))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ExtractKeywordTokens(string value)
    {
        var matches = KeywordTokenRegex.Matches(value ?? string.Empty);
        return matches
            .Select(m => NormalizeComparableText(m.Value))
            .Where(v => !string.IsNullOrWhiteSpace(v) && !KeywordStopWords.Contains(v))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeComparableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");
        normalized = normalized.Replace("（", "(").Replace("）", ")");
        return normalized;
    }

    private static double ComputeOverlapRatio(HashSet<string> sourceTokens, HashSet<string> candidateTokens)
    {
        if (sourceTokens.Count == 0 || candidateTokens.Count == 0)
            return 0;

        var overlap = sourceTokens.Intersect(candidateTokens, StringComparer.OrdinalIgnoreCase).Count();
        return overlap / (double)sourceTokens.Count;
    }

    private static bool HasText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    public async Task<Dictionary<string, double>> ComputeSimilarityAsync(
        string text1,
        string text2,
        MatchingConfig? config = null)
    {
        config ??= new MatchingConfig();
        var embedding1 = await _embeddingService.GenerateEmbeddingAsync(text1, config.EmbeddingServiceId);
        var embedding2 = await _embeddingService.GenerateEmbeddingAsync(text2, config.EmbeddingServiceId);
        var score = _embeddingService.ComputeSimilarity(embedding1, embedding2);

        return new Dictionary<string, double>
        {
            ["Embedding"] = score,
            ["Total"] = score
        };
    }

    private sealed class EvaluatedCandidate
    {
        public required MatchSource Source { get; init; }
        public required MatchCandidate Candidate { get; init; }
        public double EmbeddingScore { get; init; }
        public double FinalScore { get; set; }
        public double ProjectScore { get; set; }
        public double SpecificationTextScore { get; set; }
        public double NumericScore { get; set; }
        public double KeywordScore { get; set; }
        public double ConflictPenalty { get; set; }
        public string? RerankSummary { get; set; }
    }
}
