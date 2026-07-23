using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using Microsoft.Extensions.Logging;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public partial class SemanticKernelMatchingService : IMatchingService
{
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
            _logger.LogWarning("批量生成源文本 Embedding 失败: exceptionType={ExceptionType}", ex.GetType().Name);
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
            _logger.LogWarning("批量生成候选 Embedding 失败: exceptionType={ExceptionType}", ex.GetType().Name);
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

}
