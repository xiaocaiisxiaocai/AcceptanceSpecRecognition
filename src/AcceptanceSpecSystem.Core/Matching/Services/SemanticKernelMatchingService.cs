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
        string sourceText,
        IEnumerable<MatchCandidate> candidates,
        MatchingConfig? config = null)
    {
        config ??= new MatchingConfig();
        var candidateList = candidates.ToList();

        if (string.IsNullOrWhiteSpace(sourceText) || candidateList.Count == 0)
        {
            return [];
        }

        return await FindMatchesByEmbeddingAsync(sourceText, candidateList, config);
    }

    /// <summary>
    /// 使用 Embedding 向量进行匹配
    /// 候选项的 Embedding 会缓存到 candidate.Embedding，跨行复用避免重复生成
    /// </summary>
    private async Task<List<MatchResult>> FindMatchesByEmbeddingAsync(
        string sourceText,
        List<MatchCandidate> candidateList,
        MatchingConfig config)
    {
        float[] sourceEmbedding;
        try
        {
            sourceEmbedding = await _embeddingService.GenerateEmbeddingAsync(sourceText, config.EmbeddingServiceId);
        }
        catch (AiServiceUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "生成源文本 Embedding 失败");
            throw new AiServiceUnavailableException("Embedding 服务不可用", innerException: ex);
        }

        // 只对尚未生成 Embedding 的候选项调用远程 API，已有的直接复用
        var missingIndices = new List<int>();
        for (var i = 0; i < candidateList.Count; i++)
        {
            if (candidateList[i].Embedding == null)
                missingIndices.Add(i);
        }

        if (missingIndices.Count > 0)
        {
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
                _logger.LogWarning(ex, "生成候选 Embedding 失败");
                throw new AiServiceUnavailableException("Embedding 服务不可用", innerException: ex);
            }

            // 将生成的 Embedding 回写到候选项，后续行可直接复用
            for (var j = 0; j < missingIndices.Count && j < newEmbeddings.Count; j++)
            {
                candidateList[missingIndices[j]].Embedding = newEmbeddings[j];
            }

            _logger.LogInformation("生成 {Count}/{Total} 个候选项 Embedding（复用 {Cached} 个已缓存）",
                missingIndices.Count, candidateList.Count, candidateList.Count - missingIndices.Count);
        }
        else
        {
            _logger.LogDebug("全部 {Count} 个候选项 Embedding 已缓存，跳过远程调用", candidateList.Count);
        }

        var results = new List<MatchResult>();
        for (var i = 0; i < candidateList.Count; i++)
        {
            var candidate = candidateList[i];
            var embedding = candidate.Embedding ?? Array.Empty<float>();
            var score = _embeddingService.ComputeSimilarity(sourceEmbedding, embedding);
            var scoreDetails = new Dictionary<string, double>
            {
                ["Embedding"] = score
            };

            if (score >= config.MinScoreThreshold)
            {
                results.Add(new MatchResult
                {
                    SourceText = sourceText,
                    MatchedText = candidate.CombinedText,
                    MatchedSpecId = candidate.SpecId,
                    MatchedProject = candidate.Project,
                    MatchedSpecification = candidate.Specification,
                    MatchedAcceptance = candidate.Acceptance,
                    MatchedRemark = candidate.Remark,
                    Score = score,
                    ScoreDetails = scoreDetails
                });
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => HasText(r.MatchedAcceptance))
            .ThenByDescending(r => HasText(r.MatchedRemark))
            .ThenByDescending(r => r.MatchedSpecId ?? 0)
            .Take(1)
            .ToList();
    }

    /// <summary>
    /// 批量匹配：一次性生成所有 Embedding 后计算相似度，大幅减少 API 调用次数
    /// 注意：不会静默降级到文本相似度，Embedding 不可用时直接抛出异常
    /// </summary>
    public async Task<BatchMatchResult> BatchMatchAsync(
        IEnumerable<string> sourceTexts,
        IEnumerable<MatchCandidate> candidates,
        MatchingConfig? config = null)
    {
        config ??= new MatchingConfig();
        var sourceTextList = sourceTexts.ToList();
        var candidateList = candidates.ToList();

        if (sourceTextList.Count == 0)
            return new BatchMatchResult();

        return await BatchMatchByEmbeddingAsync(sourceTextList, candidateList, config);
    }

    /// <summary>
    /// 批量 Embedding 匹配：
    /// 步骤1 - 一次性批量生成所有源文本 Embedding
    /// 步骤2 - 一次性批量生成所有缺失候选 Embedding（复用已有缓存）
    /// 步骤3 - CPU 并行计算所有相似度
    /// </summary>
    private async Task<BatchMatchResult> BatchMatchByEmbeddingAsync(
        List<string> sourceTextList,
        List<MatchCandidate> candidateList,
        MatchingConfig config)
    {
        // 步骤1: 批量生成源文本 Embedding（N个源文本 → 1次 API 调用）
        List<float[]> sourceEmbeddings;
        try
        {
            sourceEmbeddings = await _embeddingService.GenerateEmbeddingsAsync(sourceTextList, config.EmbeddingServiceId);
            _logger.LogInformation("批量生成 {Count} 个源文本 Embedding 完成", sourceTextList.Count);
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

        // 步骤2: 批量生成缺失候选 Embedding（顺序执行，避免 DbContext 并发冲突）
        var missingIndices = new List<int>();
        for (var i = 0; i < candidateList.Count; i++)
        {
            if (candidateList[i].Embedding == null)
                missingIndices.Add(i);
        }

        if (missingIndices.Count > 0)
        {
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
        else
        {
            _logger.LogDebug("全部 {Count} 个候选项 Embedding 已缓存，跳过远程调用", candidateList.Count);
        }

        // 步骤3: 并行计算所有相似度并汇总结果（纯 CPU 计算，毫秒级）
        var result = new BatchMatchResult();
        for (var s = 0; s < sourceTextList.Count; s++)
        {
            var sourceText = sourceTextList[s];
            var sourceEmb = sourceEmbeddings[s];
            MatchResult? bestMatch = null;
            var bestScore = double.MinValue;

            for (var c = 0; c < candidateList.Count; c++)
            {
                var candidate = candidateList[c];
                var embedding = candidate.Embedding ?? Array.Empty<float>();
                var score = _embeddingService.ComputeSimilarity(sourceEmb, embedding);

                if (score < config.MinScoreThreshold)
                    continue;

                if (ShouldReplaceBestCandidate(score, candidate, bestScore, bestMatch))
                {
                    bestScore = score;
                    bestMatch = new MatchResult
                    {
                        SourceText = sourceText,
                        MatchedText = candidate.CombinedText,
                        MatchedSpecId = candidate.SpecId,
                        MatchedProject = candidate.Project,
                        MatchedSpecification = candidate.Specification,
                        MatchedAcceptance = candidate.Acceptance,
                        MatchedRemark = candidate.Remark,
                        Score = score,
                        ScoreDetails = new Dictionary<string, double> { ["Embedding"] = score }
                    };
                }
            }

            result.Results.Add(bestMatch ?? new MatchResult { SourceText = sourceText, Score = 0 });
        }

        return result;
    }

    private static bool ShouldReplaceBestCandidate(
        double score,
        MatchCandidate candidate,
        double bestScore,
        MatchResult? bestMatch)
    {
        if (bestMatch == null)
            return true;

        if (score > bestScore + ScoreTieEpsilon)
            return true;

        if (Math.Abs(score - bestScore) > ScoreTieEpsilon)
            return false;

        var currentHasAcceptance = HasText(candidate.Acceptance);
        var bestHasAcceptance = HasText(bestMatch.MatchedAcceptance);
        if (currentHasAcceptance != bestHasAcceptance)
            return currentHasAcceptance;

        var currentHasRemark = HasText(candidate.Remark);
        var bestHasRemark = HasText(bestMatch.MatchedRemark);
        if (currentHasRemark != bestHasRemark)
            return currentHasRemark;

        return candidate.SpecId > (bestMatch.MatchedSpecId ?? 0);
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
}
