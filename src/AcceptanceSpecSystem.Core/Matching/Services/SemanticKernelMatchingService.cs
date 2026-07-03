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
public partial class SemanticKernelMatchingService : IMatchingService
{
    private static readonly Regex ProjectCodeRegex = new(@"(?<![a-z0-9])([a-z]\d{2,4})(?![a-z0-9])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const double ScoreTieEpsilon = 1e-9;
    private const int TopCandidateLimit = 3;
    private const double ExactTextMatchThreshold = 0.99;
    private const double NearTextMatchThreshold = 0.88;
    private const double ProjectExactRescueEmbeddingSlack = 0.15;
    private const double ProjectCodeConflictPenaltyScore = 0.20;
    // 语义等价救援：项目精确匹配时允许 Embedding 低至此阈值，交由 LLM 裁决（单位换算/品牌中英文等场景）
    private const double SemanticEquivalenceRescueEmbeddingThreshold = 0.70;
    // 骨架相似救援：数值不同但规格"骨架"（去数值后的结构）一致时，允许 Embedding 低至此阈值进入 LLM 视野，
    // 覆盖"3000rpm vs 50r/s"这类单位换算后等价、Embedding 却偏低被召回层丢弃的候选。
    private const double SkeletonRescueEmbeddingThreshold = 0.50;
    private readonly IEmbeddingService _embeddingService;
    private readonly IMatchEvidenceBuilder _evidenceBuilder;
    private readonly ISpecCanonicalizer _canonicalizer;
    private readonly ILlmCandidateRerankService? _llmCandidateRerankService;
    private readonly ILlmEquivalenceAdjudicationService? _llmEquivalenceAdjudicationService;
    private readonly ILogger<SemanticKernelMatchingService> _logger;

    public SemanticKernelMatchingService(
        IEmbeddingService embeddingService,
        ILogger<SemanticKernelMatchingService> logger,
        IMatchEvidenceBuilder? evidenceBuilder = null,
        ILlmCandidateRerankService? llmCandidateRerankService = null,
        ILlmEquivalenceAdjudicationService? llmEquivalenceAdjudicationService = null,
        ISpecCanonicalizer? canonicalizer = null)
    {
        _embeddingService = embeddingService;
        _canonicalizer = canonicalizer ?? new SpecCanonicalizer();
        _evidenceBuilder = evidenceBuilder ?? new MatchEvidenceBuilder(new SemanticConflictScanner(_canonicalizer));
        _llmCandidateRerankService = llmCandidateRerankService;
        _llmEquivalenceAdjudicationService = llmEquivalenceAdjudicationService;
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
        MatchingConfig? config = null,
        IProgress<BatchMatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        config ??= new MatchingConfig();
        var sourceList = sources.ToList();
        var candidateList = candidates.ToList();

        if (sourceList.Count == 0)
            return new BatchMatchResult();

        return await BatchMatchByEmbeddingAsync(sourceList, candidateList, config, progress, cancellationToken);
    }
}
