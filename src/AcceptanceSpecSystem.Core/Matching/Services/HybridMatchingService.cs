using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// 兼容旧命名的匹配服务实现（已切换为 Embedding 主链路）
/// </summary>
[Obsolete("已被 SemanticKernelMatchingService 替代，仅用于兼容编译")]
public class HybridMatchingService : IMatchingService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<HybridMatchingService> _logger;

    public HybridMatchingService(IEmbeddingService embeddingService, ILogger<HybridMatchingService> logger)
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

        var candidateTexts = candidateList.Select(c => c.CombinedText).ToList();
        List<float[]> candidateEmbeddings;
        try
        {
            candidateEmbeddings = await _embeddingService.GenerateEmbeddingsAsync(candidateTexts, config.EmbeddingServiceId);
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

        var results = new List<MatchResult>();
        for (var i = 0; i < candidateList.Count; i++)
        {
            var candidate = candidateList[i];
            var embedding = candidateEmbeddings.Count > i ? candidateEmbeddings[i] : Array.Empty<float>();
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
                    Score = score,
                    ScoreDetails = scoreDetails
                });
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(1)
            .ToList();
    }

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
            var bestMatch = matches.FirstOrDefault();
            if (bestMatch != null)
            {
                result.Results.Add(bestMatch);
            }
            else
            {
                result.Results.Add(new MatchResult
                {
                    SourceText = sourceText,
                    Score = 0
                });
            }
        }

        return result;
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
