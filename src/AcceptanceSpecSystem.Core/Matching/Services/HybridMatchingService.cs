using AcceptanceSpecSystem.Core.Matching.Algorithms;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// 混合匹配服务实现
/// 结合多种相似度算法进行综合匹配
/// </summary>
public class HybridMatchingService : IMatchingService
{
    private readonly Dictionary<string, ISimilarityAlgorithm> _algorithms;
    private readonly IEmbeddingService _embeddingService;

    /// <summary>
    /// 创建混合匹配服务实例
    /// </summary>
    public HybridMatchingService(IEmbeddingService? embeddingService = null)
    {
        _algorithms = new Dictionary<string, ISimilarityAlgorithm>
        {
            ["Levenshtein"] = new LevenshteinSimilarity(),
            ["Jaccard"] = new JaccardSimilarity(),
            ["Cosine"] = new CosineSimilarity()
        };

        _embeddingService = embeddingService ?? new DefaultEmbeddingService();
    }

    /// <summary>
    /// 为单条源文本在候选集中查找匹配结果。
    /// </summary>
    /// <param name="sourceText">源文本</param>
    /// <param name="candidates">候选项集合</param>
    /// <param name="config">匹配配置（可选）</param>
    /// <returns>匹配结果列表（按得分降序，最多返回 MaxResults）</returns>
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

        var results = new List<MatchResult>();
        var weights = config.GetNormalizedWeights();

        // 如果启用Embedding且服务可用，预先计算源文本的Embedding
        float[]? sourceEmbedding = null;
        if (config.UseEmbedding && _embeddingService.IsAvailable)
        {
            try
            {
                sourceEmbedding = await _embeddingService.GenerateEmbeddingAsync(sourceText);
            }
            catch
            {
                // Embedding计算失败，降级为不使用Embedding
                weights.Remove("Embedding");
                NormalizeWeights(weights);
            }
        }

        foreach (var candidate in candidateList)
        {
            var scoreDetails = new Dictionary<string, double>();
            double totalScore = 0;

            // 计算各算法得分
            foreach (var (algorithmName, weight) in weights)
            {
                if (algorithmName == "Embedding")
                {
                    // Embedding相似度单独处理
                    if (sourceEmbedding != null)
                    {
                        try
                        {
                            var candidateEmbedding = candidate.Embedding;
                            if (candidateEmbedding == null)
                            {
                                candidateEmbedding = await _embeddingService.GenerateEmbeddingAsync(candidate.CombinedText);
                            }
                            var embeddingScore = _embeddingService.ComputeSimilarity(sourceEmbedding, candidateEmbedding);
                            scoreDetails[algorithmName] = embeddingScore;
                            totalScore += embeddingScore * weight;
                        }
                        catch
                        {
                            // 单个Embedding计算失败，跳过
                            scoreDetails[algorithmName] = 0;
                        }
                    }
                }
                else if (_algorithms.TryGetValue(algorithmName, out var algorithm))
                {
                    var score = algorithm.Calculate(sourceText, candidate.CombinedText);
                    scoreDetails[algorithmName] = score;
                    totalScore += score * weight;
                }
            }

            // 只添加超过阈值的结果
            if (totalScore >= config.MinScoreThreshold)
            {
                results.Add(new MatchResult
                {
                    SourceText = sourceText,
                    MatchedText = candidate.CombinedText,
                    MatchedSpecId = candidate.SpecId,
                    MatchedProject = candidate.Project,
                    MatchedSpecification = candidate.Specification,
                    MatchedAcceptance = candidate.Acceptance,
                    Score = totalScore,
                    ScoreDetails = scoreDetails
                });
            }
        }

        // 按得分降序排列，取前N个
        return results
            .OrderByDescending(r => r.Score)
            .Take(config.MaxResults)
            .ToList();
    }

    /// <summary>
    /// 批量匹配：对每条源文本在候选集中选择最佳匹配。
    /// </summary>
    /// <param name="sourceTexts">源文本集合</param>
    /// <param name="candidates">候选项集合</param>
    /// <param name="config">匹配配置（可选）</param>
    /// <returns>批量匹配结果</returns>
    public async Task<BatchMatchResult> BatchMatchAsync(
        IEnumerable<string> sourceTexts,
        IEnumerable<MatchCandidate> candidates,
        MatchingConfig? config = null)
    {
        config ??= new MatchingConfig();
        var candidateList = candidates.ToList();
        var result = new BatchMatchResult();

        foreach (var sourceText in sourceTexts)
        {
            var matches = await FindMatchesAsync(sourceText, candidateList, config);

            // 取最佳匹配
            var bestMatch = matches.FirstOrDefault();
            if (bestMatch != null)
            {
                result.Results.Add(bestMatch);
            }
            else
            {
                // 没有匹配，添加空结果
                result.Results.Add(new MatchResult
                {
                    SourceText = sourceText,
                    Score = 0
                });
            }
        }

        return result;
    }

    /// <summary>
    /// 计算两段文本在各启用算法下的相似度明细，并计算加权总分。
    /// </summary>
    /// <param name="text1">文本1</param>
    /// <param name="text2">文本2</param>
    /// <param name="config">匹配配置（可选）</param>
    /// <returns>得分明细字典（包含 Total）</returns>
    public Task<Dictionary<string, double>> ComputeSimilarityAsync(
        string text1,
        string text2,
        MatchingConfig? config = null)
    {
        config ??= new MatchingConfig();
        var scores = new Dictionary<string, double>();

        if (config.UseLevenshtein)
        {
            scores["Levenshtein"] = _algorithms["Levenshtein"].Calculate(text1, text2);
        }

        if (config.UseJaccard)
        {
            scores["Jaccard"] = _algorithms["Jaccard"].Calculate(text1, text2);
        }

        if (config.UseCosine)
        {
            scores["Cosine"] = _algorithms["Cosine"].Calculate(text1, text2);
        }

        // 计算加权总分
        var weights = config.GetNormalizedWeights();
        double totalScore = 0;
        foreach (var (name, score) in scores)
        {
            if (weights.TryGetValue(name, out var weight))
            {
                totalScore += score * weight;
            }
        }
        scores["Total"] = totalScore;

        return Task.FromResult(scores);
    }

    /// <summary>
    /// 归一化权重
    /// </summary>
    private static void NormalizeWeights(Dictionary<string, double> weights)
    {
        var total = weights.Values.Sum();
        if (total > 0)
        {
            foreach (var key in weights.Keys.ToList())
            {
                weights[key] /= total;
            }
        }
    }
}
