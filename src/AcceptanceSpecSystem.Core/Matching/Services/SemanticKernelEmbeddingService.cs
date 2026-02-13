using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// 基于 Semantic Kernel 的 Embedding 服务实现
/// </summary>
public class SemanticKernelEmbeddingService : IEmbeddingService
{
    private readonly AiServiceSelector _selector;
    private readonly ISemanticKernelServiceFactory _factory;
    private readonly ILogger<SemanticKernelEmbeddingService> _logger;

    public SemanticKernelEmbeddingService(
        AiServiceSelector selector,
        ISemanticKernelServiceFactory factory,
        ILogger<SemanticKernelEmbeddingService> logger)
    {
        _selector = selector;
        _factory = factory;
        _logger = logger;
    }

    public bool IsAvailable => true;

    public async Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
    {
        var result = await GenerateEmbeddingInternalAsync(text, serviceId, cancellationToken);
        return result;
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        if (textList.Count == 0)
            return [];

        var candidates = await _selector.GetCandidatesAsync(AiServicePurpose.Embedding, serviceId);
        if (candidates.Count == 0)
            throw new AiServiceUnavailableException("Embedding 服务不可用");

        var errors = new List<string>();
        foreach (var cfg in candidates)
        {
            try
            {
                var generator = _factory.CreateEmbeddingGenerator(cfg);
                var results = new List<float[]>(textList.Count);
                foreach (var text in textList)
                {
                    var vector = await generator.GenerateVectorAsync(text, cancellationToken: cancellationToken);
                    results.Add(vector.ToArray());
                }
                return results;
            }
            catch (Exception ex)
            {
                errors.Add($"{cfg.Name}: {ex.Message}");
                _logger.LogWarning(ex, "Embedding 生成失败: {Name}", cfg.Name);
            }
        }

        throw new AiServiceUnavailableException("Embedding 服务不可用", errors);
    }

    public double ComputeSimilarity(float[] embedding1, float[] embedding2)
    {
        if (embedding1.Length == 0 || embedding2.Length == 0) return 0;
        if (embedding1.Length != embedding2.Length) return 0;

        double dot = 0, norm1 = 0, norm2 = 0;
        for (var i = 0; i < embedding1.Length; i++)
        {
            dot += embedding1[i] * embedding2[i];
            norm1 += embedding1[i] * embedding1[i];
            norm2 += embedding2[i] * embedding2[i];
        }

        if (norm1 == 0 || norm2 == 0) return 0;
        var cos = dot / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
        return Math.Clamp(cos, 0, 1);
    }

    private async Task<float[]> GenerateEmbeddingInternalAsync(string text, int? serviceId, CancellationToken cancellationToken)
    {
        var candidates = await _selector.GetCandidatesAsync(AiServicePurpose.Embedding, serviceId);
        if (candidates.Count == 0)
            throw new AiServiceUnavailableException("Embedding 服务不可用");

        var errors = new List<string>();
        foreach (var cfg in candidates)
        {
            try
            {
                var generator = _factory.CreateEmbeddingGenerator(cfg);
                var vector = await generator.GenerateVectorAsync(text, cancellationToken: cancellationToken);
                return vector.ToArray();
            }
            catch (Exception ex)
            {
                errors.Add($"{cfg.Name}: {ex.Message}");
                _logger.LogWarning(ex, "Embedding 生成失败: {Name}", cfg.Name);
            }
        }

        throw new AiServiceUnavailableException("Embedding 服务不可用", errors);
    }
}
