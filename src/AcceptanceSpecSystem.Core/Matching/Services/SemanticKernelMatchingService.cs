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
    private const double ExactTextMatchThreshold = 0.99;
    private const double NearTextMatchThreshold = 0.88;
    private readonly IEmbeddingService _embeddingService;
    private readonly IMatchEvidenceBuilder _evidenceBuilder;
    private readonly ILlmCandidateRerankService? _llmCandidateRerankService;
    private readonly ILlmEquivalenceAdjudicationService? _llmEquivalenceAdjudicationService;
    private readonly ILogger<SemanticKernelMatchingService> _logger;

    public SemanticKernelMatchingService(
        IEmbeddingService embeddingService,
        ILogger<SemanticKernelMatchingService> logger,
        IMatchEvidenceBuilder? evidenceBuilder = null,
        ILlmCandidateRerankService? llmCandidateRerankService = null,
        ILlmEquivalenceAdjudicationService? llmEquivalenceAdjudicationService = null)
    {
        _embeddingService = embeddingService;
        _evidenceBuilder = evidenceBuilder ?? new MatchEvidenceBuilder();
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
        IProgress<BatchMatchProgress>? progress = null)
    {
        config ??= new MatchingConfig();
        var sourceList = sources.ToList();
        var candidateList = candidates.ToList();

        if (sourceList.Count == 0)
            return new BatchMatchResult();

        return await BatchMatchByEmbeddingAsync(sourceList, candidateList, config, progress);
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
        IProgress<BatchMatchProgress>? progress)
    {
        var orderedResults = new MatchResult[sourceList.Count];
        var exactMatchLookup = BuildExactMatchLookup(candidateList);
        var pendingSourceIndices = new List<int>(sourceList.Count);

        for (var index = 0; index < sourceList.Count; index++)
        {
            var source = sourceList[index];
            if (TryBuildExactMatchResult(source, exactMatchLookup, config, out var exactMatchResult))
            {
                orderedResults[index] = exactMatchResult;
                continue;
            }

            pendingSourceIndices.Add(index);
        }

        var exactMatchedCount = sourceList.Count - pendingSourceIndices.Count;
        if (exactMatchedCount > 0)
        {
            _logger.LogInformation(
                "批量匹配命中 {Count} 行项目/规格精确一致，已跳过 Embedding 与 AI 裁决",
                exactMatchedCount);
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

        List<float[]> sourceEmbeddings;
        try
        {
            sourceEmbeddings = await _embeddingService.GenerateEmbeddingsAsync(
                pendingSourceIndices.Select(index => sourceList[index].CombinedText),
                config.EmbeddingServiceId);
            EnsureEmbeddingBatchPayload(sourceEmbeddings, pendingSourceIndices.Count, "源文本");
            _logger.LogInformation(
                "批量生成 {Count} 个源文本 Embedding 完成（精确命中直达 {ExactCount} 行）",
                pendingSourceIndices.Count,
                exactMatchedCount);
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

        var completedItems = exactMatchedCount;
        var maxParallelism = Math.Clamp(config.LlmParallelism, 1, 10);

        progress?.Report(new BatchMatchProgress
        {
            Stage = "matching",
            StageText = "正在逐行执行匹配与 AI 裁决",
            DetailText = exactMatchedCount > 0
                ? $"精确命中 {exactMatchedCount} 行，剩余 {pendingSourceIndices.Count} 行执行语义匹配"
                : $"共 {sourceList.Count} 行待处理",
            CompletedItems = exactMatchedCount,
            TotalItems = sourceList.Count
        });

        await Parallel.ForEachAsync(
            Enumerable.Range(0, pendingSourceIndices.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism
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
                var match = await SelectBestCandidateAsync(source, eligibleCandidates, config);
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

        EnsureEmbeddingBatchPayload(newEmbeddings, missingIndices.Count, "候选项");

        for (var j = 0; j < missingIndices.Count && j < newEmbeddings.Count; j++)
        {
            candidateList[missingIndices[j]].Embedding = newEmbeddings[j];
        }

        _logger.LogInformation("生成 {Count}/{Total} 个候选项 Embedding（复用 {Cached} 个已缓存）",
            missingIndices.Count, candidateList.Count, candidateList.Count - missingIndices.Count);
    }

    private Dictionary<string, MatchCandidate> BuildExactMatchLookup(IEnumerable<MatchCandidate> candidateList)
    {
        var lookup = new Dictionary<string, MatchCandidate>(StringComparer.Ordinal);
        foreach (var candidate in candidateList)
        {
            var key = BuildExactMatchKey(candidate.Project, candidate.Specification);
            if (lookup.TryGetValue(key, out var existing) && !ShouldReplaceExactMatchCandidate(existing, candidate))
            {
                continue;
            }

            lookup[key] = candidate;
        }

        return lookup;
    }

    private bool TryBuildExactMatchResult(
        MatchSource source,
        IReadOnlyDictionary<string, MatchCandidate> exactMatchLookup,
        MatchingConfig config,
        out MatchResult result)
    {
        var key = BuildExactMatchKey(source.Project, source.Specification);
        if (!exactMatchLookup.TryGetValue(key, out var candidate))
        {
            result = null!;
            return false;
        }

        var exactCandidate = new EvaluatedCandidate
        {
            Source = source,
            Candidate = candidate,
            EmbeddingScore = 1.0,
            ProjectScore = 1.0,
            SpecificationTextScore = 1.0,
            FinalScore = 1.0
        };

        exactCandidate.Evidence = _evidenceBuilder.Build(source, candidate);
        exactCandidate.NumericScore = ComputeNumericScore(source, exactCandidate);
        exactCandidate.Issues = BuildCandidateIssues(source, exactCandidate);
        exactCandidate.FinalScore = ComputeFinalScore(exactCandidate);
        exactCandidate.LlmEquivalence = CreateExactMatchEquivalenceResult();
        exactCandidate.SelectionMode = MatchSelectionMode.ExactShortcut;
        exactCandidate.SelectionSummary = "项目与规格精确一致，直接命中";
        exactCandidate.RerankSummary = AppendEquivalenceSummary(
            BuildRerankSummary(exactCandidate),
            exactCandidate.LlmEquivalence);

        result = BuildMatchResult(
            exactCandidate,
            recalledCandidateCount: 1,
            isAmbiguous: false,
            scoreGap: null,
            config.HighConfidenceThreshold,
            orderedCandidates: [exactCandidate]);
        return true;
    }

    private static LlmEquivalenceAdjudicationResult CreateExactMatchEquivalenceResult()
    {
        return new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 1,
            Reason = "项目与规格文本完全一致，已直接视为等价"
        };
    }

    private static bool ShouldReplaceExactMatchCandidate(MatchCandidate current, MatchCandidate incoming)
    {
        if (HasText(incoming.Acceptance) != HasText(current.Acceptance))
        {
            return HasText(incoming.Acceptance);
        }

        if (HasText(incoming.Remark) != HasText(current.Remark))
        {
            return HasText(incoming.Remark);
        }

        return incoming.SpecId > current.SpecId;
    }

    private static string BuildExactMatchKey(string? project, string? specification)
    {
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

            if (!ShouldKeepCandidate(
                    embeddingScore,
                    projectScore,
                    specificationTextScore,
                    config.MinScoreThreshold))
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

    private async Task<MatchResult?> SelectBestCandidateAsync(
        MatchSource source,
        List<EvaluatedCandidate> eligibleCandidates,
        MatchingConfig config)
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
        var best = await SelectCurrentBestCandidateAsync(source, locallyOrdered, config);
        var ordered = ReorderSelectedCandidateFirst(locallyOrdered, best);

        await ApplyLlmEquivalenceAdjudicationAsync(source, best, config);

        var second = ordered.Count > 1 ? ordered[1] : null;
        double? scoreGap = second == null ? null : best.FinalScore - second.FinalScore;
        var isAmbiguous = ShouldMarkAsAmbiguous(best, second, scoreGap, config.AmbiguityMargin);

        return BuildMatchResult(
            best,
            recalled.Count,
            isAmbiguous,
            scoreGap,
            config.HighConfidenceThreshold,
            orderedCandidates: ordered);
    }

    private async Task<EvaluatedCandidate> SelectCurrentBestCandidateAsync(
        MatchSource source,
        IReadOnlyList<EvaluatedCandidate> orderedCandidates,
        MatchingConfig config)
    {
        var localBest = orderedCandidates[0];
        localBest.SelectionSummary ??= "沿用本地 Top1 排序结果";

        if (_llmCandidateRerankService == null || orderedCandidates.Count <= 1)
        {
            return localBest;
        }

        try
        {
            var rerankResult = await _llmCandidateRerankService.RerankAsync(
                new LlmCandidateRerankRequest
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
                });

            if (rerankResult == null)
            {
                localBest.SelectionSummary = "AI 重排未返回有效结果，已沿用本地 Top1";
                return localBest;
            }

            var selected = orderedCandidates.FirstOrDefault(candidate =>
                candidate.Candidate.SpecId == rerankResult.SelectedSpecId);
            if (selected == null)
            {
                localBest.SelectionSummary = "AI 重排返回非法候选，已沿用本地 Top1";
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI 候选重排失败，已沿用本地 Top1");
            localBest.SelectionSummary = "AI 重排失败，已沿用本地 Top1";
            return localBest;
        }
    }

    private async Task ApplyLlmEquivalenceAdjudicationAsync(
        MatchSource source,
        EvaluatedCandidate best,
        MatchingConfig config)
    {
        if (_llmEquivalenceAdjudicationService == null ||
            !ShouldRunLlmEquivalenceAdjudication(best, config))
        {
            return;
        }

        try
        {
            var result = await _llmEquivalenceAdjudicationService.AdjudicateAsync(
                new LlmEquivalenceAdjudicationRequest
                {
                    SourceProject = source.Project,
                    SourceSpecification = source.Specification,
                    CandidateProject = best.Candidate.Project,
                    CandidateSpecification = best.Candidate.Specification,
                    CurrentDecision = "manualReview",
                    ScoreDetails = CreateScoreDetails(best),
                    EvidenceSummary = [.. (best.Evidence?.Summary ?? [])],
                    ConflictSummary = [.. (best.Evidence?.Conflicts ?? [])],
                    LlmServiceId = config.LlmServiceId
                });

            best.LlmEquivalence = result ?? new LlmEquivalenceAdjudicationResult
            {
                Verdict = LlmEquivalenceVerdict.Uncertain,
                ReasonType = LlmEquivalenceReasonType.Uncertain,
                Confidence = 0,
                Reason = "AI 等价裁决未返回有效结果，已回退为人工确认"
            };
            best.RerankSummary = AppendEquivalenceSummary(best.RerankSummary, best.LlmEquivalence);
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
        double highConfidenceThreshold,
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
            Decision = DetermineDecision(candidate, isAmbiguous),
            HighConfidenceThreshold = MatchingThresholds.NormalizeHighConfidenceThreshold(highConfidenceThreshold),
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
            ["NumberUnit"] = candidate.NumericScore
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
        var finalScore =
            candidate.EmbeddingScore * 0.55 +
            candidate.ProjectScore * 0.15 +
            candidate.SpecificationTextScore * 0.15 +
            candidate.NumericScore * 0.15;

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

    private static double ComputeNumericScore(MatchSource source, EvaluatedCandidate candidate)
    {
        var sourceText = NormalizeComparableText(source.Specification);
        var candidateText = NormalizeComparableText(candidate.Candidate.Specification);

        if (string.IsNullOrWhiteSpace(sourceText) && string.IsNullOrWhiteSpace(candidateText))
            return 1.0;

        if (!string.IsNullOrWhiteSpace(sourceText) && sourceText == candidateText)
            return 1.0;

        return 0.5;
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

    private static void RefreshCandidateScores(MatchSource source, IReadOnlyList<EvaluatedCandidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            candidate.Issues = BuildCandidateIssues(source, candidate);
            candidate.FinalScore = ComputeFinalScore(candidate);
            candidate.RerankSummary = BuildRerankSummary(candidate);
        }
    }

    private static MatchDecision DetermineDecision(EvaluatedCandidate candidate, bool isAmbiguous)
    {
        if (candidate.LlmEquivalence?.Verdict is LlmEquivalenceVerdict.Different or LlmEquivalenceVerdict.Uncertain)
            return MatchDecision.ManualReview;

        if (RequiresManualReview(candidate.Evidence))
            return MatchDecision.ManualReview;

        if (isAmbiguous)
            return MatchDecision.ManualReview;

        if (candidate.LlmEquivalence?.Verdict == LlmEquivalenceVerdict.Equivalent)
            return MatchDecision.AutoApply;

        return MatchDecision.ManualReview;
    }

    private static bool ShouldRunLlmEquivalenceAdjudication(
        EvaluatedCandidate candidate,
        MatchingConfig _)
    {
        var shouldRunByFinalScore = candidate.FinalScore >= MatchingThresholds.MediumConfidenceScore;
        var shouldRunByEmbedding = candidate.EmbeddingScore >= MatchingThresholds.MediumConfidenceScore;
        if (!shouldRunByFinalScore && !shouldRunByEmbedding)
            return false;

        // 去掉本地品牌/单位/方向规则后，允许高 embedding 候选进入 AI 复议，
        // 由模型决定是否等价，避免因为本地文本分过低而错过真实语义相近样本。
        return true;
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

        if (evidence.Identifiers.Any(item =>
                item.Relation is EvidenceRelation.Overlap or EvidenceRelation.ParentChild or EvidenceRelation.PossiblyRelated))
            return true;

        if (evidence.Entities.Any(item =>
                item.Relation is EvidenceRelation.Overlap or EvidenceRelation.ParentChild or EvidenceRelation.PossiblyRelated))
            return true;

        return false;
    }

    private static bool ShouldKeepCandidate(
        double embeddingScore,
        double projectScore,
        double specificationTextScore,
        double minScoreThreshold)
    {
        return embeddingScore >= minScoreThreshold ||
               IsExactTextRescueCandidate(projectScore, specificationTextScore);
    }

    private static bool IsExactTextRescueCandidate(double projectScore, double specificationTextScore)
    {
        if (specificationTextScore >= ExactTextMatchThreshold)
            return true;

        return projectScore >= ExactTextMatchThreshold &&
               specificationTextScore >= NearTextMatchThreshold;
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

    private static List<MatchIssue> BuildCandidateIssues(MatchSource source, EvaluatedCandidate candidate)
    {
        return candidate.Evidence?.Issues.ToList() ?? [];
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

    private sealed class EvaluatedCandidate
    {
        public required MatchSource Source { get; init; }
        public required MatchCandidate Candidate { get; init; }
        public double EmbeddingScore { get; init; }
        public double FinalScore { get; set; }
        public double ProjectScore { get; set; }
        public double SpecificationTextScore { get; set; }
        public double NumericScore { get; set; }
        public string? RerankSummary { get; set; }
        public MatchSelectionMode SelectionMode { get; set; } = MatchSelectionMode.EmbeddingTop1;
        public string? SelectionSummary { get; set; }
        public MatchEvidence? Evidence { get; set; }
        public List<MatchIssue>? Issues { get; set; }
        public LlmEquivalenceAdjudicationResult? LlmEquivalence { get; set; }
    }

}
