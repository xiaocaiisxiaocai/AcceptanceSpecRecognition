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

    /// <summary>
    /// 批量 Embedding 匹配：
    /// 步骤1 - 一次性批量生成所有源文本 Embedding
    /// 步骤2 - 一次性批量生成所有缺失候选 Embedding（复用已有缓存）
    /// 步骤3 - 对每条源文本执行统一证据裁决
    /// </summary>
    private async Task<BatchMatchResult> BatchMatchByEmbeddingAsync(
        List<MatchSource> sourceList,
        List<MatchCandidate> candidateList,
        MatchingConfig config,
        IProgress<BatchMatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        var orderedResults = new MatchResult[sourceList.Count];
        var exactMatchLookup = BuildExactMatchLookup(candidateList, config);
        // 规范化精确层：单位归一/品牌统一/同义表达/格式差异在此变成"精确命中"，
        // 无需 Embedding、无需 LLM。这是把语义等价判断从 LLM 下沉为确定性代码的核心。
        var canonicalMatchLookup = BuildCanonicalMatchLookup(candidateList, config);
        // 近似规范化层的候选侧数据（Canonicalize/骨架/数值提取均为正则重活）整批只算一次，
        // 避免每个源行对全量候选重复规范化造成 O(源×候选) 开销；全部命中前两层时完全不算。
        var canonicalSnapshots = new Lazy<List<CandidateCanonicalSnapshot>>(
            () => BuildCandidateCanonicalSnapshots(candidateList));
        var pendingSourceIndices = new List<int>(sourceList.Count);
        var canonicalShortcutCount = 0;

        for (var index = 0; index < sourceList.Count; index++)
        {
            var source = sourceList[index];
            if (TryBuildExactMatchResult(source, exactMatchLookup, config, out var exactMatchResult))
            {
                orderedResults[index] = exactMatchResult;
                continue;
            }

            if (TryBuildCanonicalMatchResult(source, canonicalMatchLookup, config, out var canonicalResult))
            {
                orderedResults[index] = canonicalResult;
                canonicalShortcutCount++;
                continue;
            }

            if (TryBuildApproximateCanonicalMatchResult(source, canonicalSnapshots.Value, config, out var approximateCanonicalResult))
            {
                orderedResults[index] = approximateCanonicalResult;
                canonicalShortcutCount++;
                continue;
            }

            pendingSourceIndices.Add(index);
        }

        var exactMatchedCount = sourceList.Count - pendingSourceIndices.Count - canonicalShortcutCount;
        if (exactMatchedCount > 0)
        {
            _logger.LogInformation(
                "批量匹配命中 {Count} 行项目/规格精确一致，已跳过 Embedding 与 AI 裁决",
                exactMatchedCount);
        }

        if (canonicalShortcutCount > 0)
        {
            _logger.LogInformation(
                "批量匹配命中 {Count} 行规范化等价（单位/品牌/同义/格式归一），已跳过 Embedding 与 AI 裁决",
                canonicalShortcutCount);
        }

        if (pendingSourceIndices.Count == 0)
        {
            progress?.Report(new BatchMatchProgress
            {
                Stage = "matching",
                StageText = "项目/规格精确命中，已跳过语义匹配",
                DetailText = $"共 {sourceList.Count} 行，全部命中精确匹配",
                CompletedItems = sourceList.Count,
                TotalItems = sourceList.Count
            });

            return new BatchMatchResult
            {
                Results = orderedResults.ToList()
            };
        }

        var shortcutMatchedCount = exactMatchedCount + canonicalShortcutCount;

        List<float[]> sourceEmbeddings;
        try
        {
            progress?.Report(new BatchMatchProgress
            {
                Stage = "embedding_source",
                StageText = "正在生成源文本语义特征",
                DetailText = shortcutMatchedCount > 0
                    ? $"共 {pendingSourceIndices.Count} 行待生成，精确命中已跳过 {shortcutMatchedCount} 行"
                    : $"共 {pendingSourceIndices.Count} 行待生成",
                CompletedItems = shortcutMatchedCount,
                TotalItems = sourceList.Count
            });

            sourceEmbeddings = await _embeddingService.GenerateEmbeddingsAsync(
                pendingSourceIndices.Select(index => GetSourceEmbeddingText(sourceList[index], config)),
                config.EmbeddingServiceId,
                cancellationToken);
            EnsureEmbeddingBatchPayload(sourceEmbeddings, pendingSourceIndices.Count, "源文本");
            _logger.LogInformation(
                "批量生成 {Count} 个源文本 Embedding 完成（精确命中直达 {ExactCount} 行）",
                pendingSourceIndices.Count,
                exactMatchedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

        progress?.Report(new BatchMatchProgress
        {
            Stage = "embedding_candidates",
            StageText = "正在加载候选语义特征",
            DetailText = "正在补全缺失的候选项 Embedding 向量",
            CompletedItems = shortcutMatchedCount,
            TotalItems = sourceList.Count
        });

        await EnsureCandidateEmbeddingsAsync(candidateList, config, cancellationToken);
        var completedItems = shortcutMatchedCount;
        var maxParallelism = Math.Clamp(config.LlmParallelism, 1, 10);

        // LLM 等价裁决全局限流预算：整批共享一个原子计数器。
        // 达到上限后剩余灰区行一律转人工，避免大批量时 LLM 拖垮整体耗时。
        var llmBudget = new LlmCallBudget(config.LlmMaxCallsPerBatch);
        var llmCircuitBreaker = new LlmCircuitBreaker(config.LlmCircuitBreakFailures);

        progress?.Report(new BatchMatchProgress
        {
            Stage = "matching",
            StageText = "正在逐行执行匹配与 AI 裁决",
            DetailText = shortcutMatchedCount > 0
                ? $"直达命中 {shortcutMatchedCount} 行，剩余 {pendingSourceIndices.Count} 行执行语义匹配"
                : $"共 {sourceList.Count} 行待处理",
            CompletedItems = shortcutMatchedCount,
            TotalItems = sourceList.Count
        });

        await Parallel.ForEachAsync(
            Enumerable.Range(0, pendingSourceIndices.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = cancellationToken
            },
            async (offset, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var index = pendingSourceIndices[offset];
                var source = sourceList[index];
                var sourceEmbedding = offset < sourceEmbeddings.Count
                    ? sourceEmbeddings[offset]
                    : Array.Empty<float>();
                var eligibleCandidates = EvaluateCandidates(source, sourceEmbedding, candidateList, config);
                var match = await SelectBestCandidateAsync(
                    source,
                    eligibleCandidates,
                    config,
                    llmBudget,
                    llmCircuitBreaker,
                    cancellationToken);
                orderedResults[index] = match ?? CreateEmptyResult(source);

                var completed = Interlocked.Increment(ref completedItems);
                progress?.Report(new BatchMatchProgress
                {
                    Stage = "matching",
                    StageText = "正在逐行执行匹配与 AI 裁决",
                    DetailText = $"已完成 {completed}/{sourceList.Count} 行",
                    CompletedItems = completed,
                    TotalItems = sourceList.Count
                });
            });

        return new BatchMatchResult
        {
            Results = orderedResults.ToList()
        };
    }

    private async Task EnsureCandidateEmbeddingsAsync(
        List<MatchCandidate> candidateList,
        MatchingConfig config,
        CancellationToken cancellationToken)
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

        var missingTexts = missingIndices.Select(i => GetCandidateEmbeddingText(candidateList[i], config)).ToList();
        List<float[]> newEmbeddings;
        try
        {
            newEmbeddings = await _embeddingService.GenerateEmbeddingsAsync(
                missingTexts,
                config.EmbeddingServiceId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

        EnsureEmbeddingBatchPayload(newEmbeddings, missingIndices.Count, "候选项");

        for (var j = 0; j < missingIndices.Count && j < newEmbeddings.Count; j++)
        {
            candidateList[missingIndices[j]].Embedding = newEmbeddings[j];
        }

        _logger.LogInformation("生成 {Count}/{Total} 个候选项 Embedding（复用 {Cached} 个已缓存）",
            missingIndices.Count, candidateList.Count, candidateList.Count - missingIndices.Count);
    }

    private Dictionary<string, List<MatchCandidate>> BuildExactMatchLookup(
        IEnumerable<MatchCandidate> candidateList,
        MatchingConfig config)
    {
        var lookup = new Dictionary<string, List<MatchCandidate>>(StringComparer.Ordinal);
        foreach (var candidate in candidateList)
        {
            var key = BuildExactMatchKey(candidate.Project, candidate.Specification, config);
            if (!lookup.TryGetValue(key, out var list))
            {
                list = [];
                lookup[key] = list;
            }

            list.Add(candidate);
        }

        SortShortcutCandidates(lookup);
        return lookup;
    }

    private bool TryBuildExactMatchResult(
        MatchSource source,
        IReadOnlyDictionary<string, List<MatchCandidate>> exactMatchLookup,
        MatchingConfig config,
        out MatchResult result)
    {
        var key = BuildExactMatchKey(source.Project, source.Specification, config);
        if (!exactMatchLookup.TryGetValue(key, out var candidatesForKey))
        {
            result = null!;
            return false;
        }

        var candidate = candidatesForKey[0];
        var isSpecificationOnly = config.MatchingMode == MatchingMode.SpecificationOnly;
        var isAmbiguous = isSpecificationOnly && candidatesForKey.Count > 1;

        var exactCandidate = new EvaluatedCandidate
        {
            Source = source,
            Candidate = candidate,
            EmbeddingScore = 1.0,
            ProjectScore = isSpecificationOnly ? 0 : 1.0,
            SpecificationTextScore = 1.0,
            FinalScore = 1.0
        };

        exactCandidate.Evidence = _evidenceBuilder.Build(source, candidate);
        exactCandidate.NumericScore = ComputeNumericScore(source, exactCandidate);
        exactCandidate.Issues = BuildCandidateIssues(source, exactCandidate);
        exactCandidate.FinalScore = ComputeFinalScore(exactCandidate);
        exactCandidate.LlmEquivalence = CreateExactMatchEquivalenceResult(config);
        exactCandidate.SelectionMode = MatchSelectionMode.ExactShortcut;
        exactCandidate.SelectionSummary = isSpecificationOnly
            ? isAmbiguous
                ? "规格精确一致，但同规格存在多条候选，需人工确认"
                : "规格精确一致，按仅规格模式直接命中"
            : "项目与规格精确一致，直接命中";
        exactCandidate.MatchBasis = isSpecificationOnly
            ? MatchBasis.Specification
            : MatchBasis.ProjectSpecification;
        exactCandidate.RerankSummary = AppendEquivalenceSummary(
            BuildRerankSummary(exactCandidate),
            exactCandidate.LlmEquivalence);

        var orderedCandidates = isSpecificationOnly
            ? BuildShortcutCandidateSnapshots(source, candidatesForKey, exactCandidate, config)
            : [exactCandidate];

        result = BuildMatchResult(
            exactCandidate,
            recalledCandidateCount: isSpecificationOnly ? candidatesForKey.Count : 1,
            isAmbiguous,
            scoreGap: null,
            config.MinScoreThreshold,
            config.HighConfidenceThreshold,
            config,
            orderedCandidates);
        return true;
    }

    private static LlmEquivalenceAdjudicationResult CreateExactMatchEquivalenceResult(MatchingConfig config)
    {
        return new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 1,
            Reason = config.MatchingMode == MatchingMode.SpecificationOnly
                ? "规格文本完全一致，已按用户选择的仅规格模式命中"
                : "项目与规格文本完全一致，已直接视为等价"
        };
    }

    /// <summary>
    /// 构建规范化精确匹配查找表。
    /// 键 = Canonicalize(项目)+Canonicalize(规格)，可吸收单位/品牌/同义/格式差异。
    /// 原文精确层会先执行；规范化层作为第二层补充，用于命中原文不完全一致但规范化后等价的候选。
    /// </summary>
    private Dictionary<string, List<MatchCandidate>> BuildCanonicalMatchLookup(
        IEnumerable<MatchCandidate> candidateList,
        MatchingConfig config)
    {
        var lookup = new Dictionary<string, List<MatchCandidate>>(StringComparer.Ordinal);
        foreach (var candidate in candidateList)
        {
            var key = BuildCanonicalMatchKey(candidate.Project, candidate.Specification, config);
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (!lookup.TryGetValue(key, out var list))
            {
                list = [];
                lookup[key] = list;
            }

            list.Add(candidate);
        }

        SortShortcutCandidates(lookup);
        return lookup;
    }

    /// <summary>
    /// 尝试用规范化精确层命中候选。命中后直接 AutoApply，跳过 Embedding 与 LLM。
    /// </summary>
    private bool TryBuildCanonicalMatchResult(
        MatchSource source,
        IReadOnlyDictionary<string, List<MatchCandidate>> canonicalMatchLookup,
        MatchingConfig config,
        out MatchResult result)
    {
        var key = BuildCanonicalMatchKey(source.Project, source.Specification, config);
        if (string.IsNullOrEmpty(key) || !canonicalMatchLookup.TryGetValue(key, out var candidatesForKey))
        {
            result = null!;
            return false;
        }

        var candidate = candidatesForKey[0];
        var isSpecificationOnly = config.MatchingMode == MatchingMode.SpecificationOnly;
        var isAmbiguous = isSpecificationOnly && candidatesForKey.Count > 1;

        var equivalence = new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 1,
            Reason = "规范化后等价（单位/品牌/同义/格式归一一致），已确定性命中"
        };

        var canonicalCandidate = new EvaluatedCandidate
        {
            Source = source,
            Candidate = candidate,
            EmbeddingScore = 1.0,
            ProjectScore = isSpecificationOnly ? 0 : 1.0,
            SpecificationTextScore = 1.0,
            FinalScore = 1.0
        };

        canonicalCandidate.Evidence = _evidenceBuilder.Build(source, candidate);
        canonicalCandidate.NumericScore = ComputeNumericScore(source, canonicalCandidate);
        canonicalCandidate.Issues = BuildCandidateIssues(source, canonicalCandidate);

        // 安全网：规范化命中后仍跑冲突扫描。理论上规范化等价不应有硬冲突，
        // 但若证据构建器扫出 hard_conflict（如数值归一边界差异），宁可转人工。
        if (HasHardConflict(canonicalCandidate.Issues))
        {
            result = null!;
            return false;
        }

        canonicalCandidate.FinalScore = ComputeFinalScore(canonicalCandidate);
        canonicalCandidate.LlmEquivalence = equivalence;
        canonicalCandidate.SelectionMode = MatchSelectionMode.ExactShortcut;
        canonicalCandidate.SelectionSummary = isSpecificationOnly && isAmbiguous
            ? "规范化后规格等价，但同规格存在多条候选，需人工确认"
            : "规范化等价（单位/品牌/同义/格式归一），确定性直接命中";
        canonicalCandidate.MatchBasis = isSpecificationOnly
            ? MatchBasis.Specification
            : MatchBasis.ProjectSpecification;
        canonicalCandidate.RerankSummary = AppendEquivalenceSummary(
            BuildRerankSummary(canonicalCandidate),
            equivalence);

        var orderedCandidates = isSpecificationOnly
            ? BuildShortcutCandidateSnapshots(source, candidatesForKey, canonicalCandidate, config)
            : [canonicalCandidate];

        result = BuildMatchResult(
            canonicalCandidate,
            recalledCandidateCount: isSpecificationOnly ? candidatesForKey.Count : 1,
            isAmbiguous,
            scoreGap: null,
            config.MinScoreThreshold,
            config.HighConfidenceThreshold,
            config,
            orderedCandidates);
        return true;
    }

    /// <summary>
    /// 候选项的近似规范化快照：规范化项目、规格骨架与可归一数值集合，整批预计算一次。
    /// </summary>
    private sealed record CandidateCanonicalSnapshot(
        MatchCandidate Candidate,
        string CanonicalProject,
        string SpecificationSkeleton,
        IReadOnlyList<(double BaseValue, string Dimension, string OriginalExpression)> NormalizedValues);

    private List<CandidateCanonicalSnapshot> BuildCandidateCanonicalSnapshots(
        IReadOnlyList<MatchCandidate> candidateList)
    {
        var snapshots = new List<CandidateCanonicalSnapshot>(candidateList.Count);
        foreach (var candidate in candidateList)
        {
            snapshots.Add(new CandidateCanonicalSnapshot(
                candidate,
                _canonicalizer.Canonicalize(candidate.Project),
                BuildCanonicalSpecificationSkeleton(candidate.Specification),
                _canonicalizer.ExtractNormalizedValues(candidate.Specification)));
        }

        return snapshots;
    }

    private bool TryBuildApproximateCanonicalMatchResult(
        MatchSource source,
        IReadOnlyList<CandidateCanonicalSnapshot> candidateSnapshots,
        MatchingConfig config,
        out MatchResult result)
    {
        result = null!;

        var sourceProject = config.MatchingMode == MatchingMode.SpecificationOnly
            ? null
            : _canonicalizer.Canonicalize(source.Project);

        var sourceValues = _canonicalizer.ExtractNormalizedValues(source.Specification);
        if (sourceValues.Count == 0)
            return false;

        var sourceSkeleton = BuildCanonicalSpecificationSkeleton(source.Specification);
        if (string.IsNullOrWhiteSpace(sourceSkeleton))
            return false;

        var candidatesForKey = candidateSnapshots
            .Where(snapshot =>
                (sourceProject == null ||
                 string.Equals(snapshot.CanonicalProject, sourceProject, StringComparison.Ordinal)) &&
                string.Equals(sourceSkeleton, snapshot.SpecificationSkeleton, StringComparison.Ordinal) &&
                NormalizedValueSetsEqual(sourceValues, snapshot.NormalizedValues))
            .Select(snapshot => snapshot.Candidate)
            .ToList();

        if (candidatesForKey.Count == 0)
            return false;

        SortShortcutCandidatesByList(candidatesForKey);
        var candidate = candidatesForKey[0];
        var isSpecificationOnly = config.MatchingMode == MatchingMode.SpecificationOnly;
        var approximateCandidate = new EvaluatedCandidate
        {
            Source = source,
            Candidate = candidate,
            EmbeddingScore = 1.0,
            ProjectScore = isSpecificationOnly ? 0 : 1.0,
            SpecificationTextScore = 1.0,
            NumericScore = 1.0,
            FinalScore = 1.0
        };

        approximateCandidate.Evidence = _evidenceBuilder.Build(source, candidate);
        approximateCandidate.Issues = BuildCandidateIssues(source, approximateCandidate);
        // 语义优先模式下，AutoApplyBlocking warning 不再拦截（与 DetermineDecision 的处理保持一致）
        if (HasHardConflict(approximateCandidate.Issues) ||
            (!config.EnableLlmSemanticPriority && HasAutoApplyBlockingWarning(approximateCandidate.Issues)))
            return false;

        approximateCandidate.LlmEquivalence = new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.SymbolEquivalent,
            Confidence = 1,
            Reason = "规范化数值在工程容差内等价，已确定性命中"
        };
        approximateCandidate.SelectionMode = MatchSelectionMode.ExactShortcut;
        approximateCandidate.SelectionSummary = isSpecificationOnly
            ? "规范化数值在工程容差内等价，已按仅规格模式确定性命中"
            : "规范化数值在工程容差内等价，确定性直接命中";
        approximateCandidate.MatchBasis = isSpecificationOnly
            ? MatchBasis.Specification
            : MatchBasis.ProjectSpecification;
        approximateCandidate.RerankSummary = AppendEquivalenceSummary(
            BuildRerankSummary(approximateCandidate),
            approximateCandidate.LlmEquivalence);

        result = BuildMatchResult(
            approximateCandidate,
            recalledCandidateCount: 1,
            isAmbiguous: false,
            scoreGap: null,
            config.MinScoreThreshold,
            config.HighConfidenceThreshold,
            config,
            [approximateCandidate]);
        return true;
    }

    private static void SortShortcutCandidates(Dictionary<string, List<MatchCandidate>> lookup)
    {
        foreach (var key in lookup.Keys.ToList())
        {
            SortShortcutCandidatesByList(lookup[key]);
        }
    }

    private static void SortShortcutCandidatesByList(List<MatchCandidate> candidates)
    {
        candidates.Sort((left, right) =>
        {
            var acceptance = HasText(right.Acceptance).CompareTo(HasText(left.Acceptance));
            if (acceptance != 0) return acceptance;
            var remark = HasText(right.Remark).CompareTo(HasText(left.Remark));
            if (remark != 0) return remark;
            return right.SpecId.CompareTo(left.SpecId);
        });
    }

    private List<EvaluatedCandidate> BuildShortcutCandidateSnapshots(
        MatchSource source,
        IReadOnlyList<MatchCandidate> candidates,
        EvaluatedCandidate primary,
        MatchingConfig config)
    {
        var snapshots = new List<EvaluatedCandidate> { primary };
        foreach (var candidate in candidates.Skip(1).Take(TopCandidateLimit - 1))
        {
            var evaluated = new EvaluatedCandidate
            {
                Source = source,
                Candidate = candidate,
                EmbeddingScore = 1.0,
                ProjectScore = config.MatchingMode == MatchingMode.SpecificationOnly ? 0 : 1.0,
                SpecificationTextScore = 1.0,
                NumericScore = 1.0,
                FinalScore = 1.0,
                SelectionMode = MatchSelectionMode.ExactShortcut,
                SelectionSummary = config.MatchingMode == MatchingMode.SpecificationOnly
                    ? "规格精确一致"
                    : "项目与规格精确一致，直接命中",
                MatchBasis = config.MatchingMode == MatchingMode.SpecificationOnly
                    ? MatchBasis.Specification
                    : MatchBasis.ProjectSpecification,
                Evidence = _evidenceBuilder.Build(source, candidate),
                LlmEquivalence = null
            };
            evaluated.Issues = BuildCandidateIssues(source, evaluated);
            evaluated.RerankSummary = BuildRerankSummary(evaluated);
            snapshots.Add(evaluated);
        }

        return snapshots;
    }

    /// <summary>
    /// 构建规范化匹配键。若项目与规格均为空，返回空字符串，由调用方跳过。
    /// </summary>
    private string BuildCanonicalMatchKey(
        string? project,
        string? specification,
        MatchingConfig config)
    {
        var canonicalProject = _canonicalizer.Canonicalize(project);
        var canonicalSpecification = _canonicalizer.Canonicalize(specification);

        var canonicalKey = config.MatchingMode == MatchingMode.SpecificationOnly
            ? canonicalSpecification
            : $"{canonicalProject}\n{canonicalSpecification}";

        if (string.IsNullOrWhiteSpace(canonicalKey.Replace("\n", string.Empty)))
        {
            return string.Empty;
        }

        return canonicalKey;
    }

    private static string BuildExactMatchKey(
        string? project,
        string? specification,
        MatchingConfig config)
    {
        if (config.MatchingMode == MatchingMode.SpecificationOnly)
        {
            return NormalizeComparableText(specification);
        }

        return $"{NormalizeComparableText(project)}\n{NormalizeComparableText(specification)}";
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
        var sourceText = NormalizeComparableText(source.Specification);
        var candidateText = NormalizeComparableText(candidate.Candidate.Specification);

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

    /// <summary>
    /// 单批次 LLM 调用预算（线程安全）。
    /// 各并行行共享同一实例，通过原子递减实现全局限流；预算耗尽后灰区行一律转人工。
    /// </summary>
    private sealed class LlmCallBudget
    {
        private int _remaining;

        public LlmCallBudget(int maxCalls)
        {
            _remaining = Math.Max(0, maxCalls);
        }

        /// <summary>
        /// 尝试占用一次 LLM 调用配额。成功返回 true 并扣减；预算耗尽返回 false。
        /// </summary>
        public bool TryConsume()
        {
            // 原子地将剩余值减 1，仅当减之前 > 0 才算成功
            while (true)
            {
                var current = Volatile.Read(ref _remaining);
                if (current <= 0)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _remaining, current - 1, current) == current)
                {
                    return true;
                }
            }
        }
    }

    /// <summary>
    /// LLM 熔断器：按"连续失败"计数，任意一次成功即复位。
    /// 避免大批量低失败率场景下累计失败误触发永久熔断。
    /// </summary>
    private sealed class LlmCircuitBreaker
    {
        private readonly int _failureThreshold;
        private int _consecutiveFailureCount;
        private int _isOpen;

        public LlmCircuitBreaker(int failureThreshold)
        {
            _failureThreshold = Math.Clamp(failureThreshold, 3, 200);
        }

        public bool IsOpen => Volatile.Read(ref _isOpen) == 1;

        public void RecordFailure()
        {
            if (Interlocked.Increment(ref _consecutiveFailureCount) >= _failureThreshold)
            {
                Volatile.Write(ref _isOpen, 1);
            }
        }

        public void RecordSuccess()
        {
            Interlocked.Exchange(ref _consecutiveFailureCount, 0);
        }
    }

    private readonly record struct LlmCallExecution<T>(T? Result, bool Failed, bool BudgetExhausted);

    private sealed class EvaluatedCandidate
    {
        public required MatchSource Source { get; init; }
        public required MatchCandidate Candidate { get; init; }
        public double EmbeddingScore { get; init; }
        public double FinalScore { get; set; }
        public double ProjectScore { get; set; }
        public double SpecificationTextScore { get; set; }
        public double NumericScore { get; set; }
        public double ProjectCodeConflictPenalty { get; set; }
        public bool IsSkeletonRescue { get; init; }
        public string? RerankSummary { get; set; }
        public MatchSelectionMode SelectionMode { get; set; } = MatchSelectionMode.EmbeddingTop1;
        public string? SelectionSummary { get; set; }
        public MatchBasis MatchBasis { get; set; } = MatchBasis.ProjectSpecification;
        public MatchEvidence? Evidence { get; set; }
        public List<MatchIssue>? Issues { get; set; }
        public LlmEquivalenceAdjudicationResult? LlmEquivalence { get; set; }
    }

}
