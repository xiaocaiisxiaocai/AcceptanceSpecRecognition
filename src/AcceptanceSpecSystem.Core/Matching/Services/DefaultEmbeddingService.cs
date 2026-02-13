using AcceptanceSpecSystem.Core.Matching.Interfaces;

namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// 默认Embedding服务实现
/// 在没有配置AI服务时提供占位功能
/// </summary>
public class DefaultEmbeddingService : IEmbeddingService
{
    /// <summary>
    /// 当前 Embedding 服务是否可用。
    /// 默认实现始终不可用，用于在未配置 AI 服务时明确提示。
    /// </summary>
    public bool IsAvailable => false;

    /// <summary>
    /// 生成单条文本的向量表示。
    /// 默认实现不可用，会抛出异常提示需要先配置 AI 服务。
    /// </summary>
    /// <param name="text">文本</param>
    /// <param name="serviceId">指定服务ID（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>向量</returns>
    public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Embedding服务不可用，请先配置AI服务");
    }

    /// <summary>
    /// 批量生成文本的向量表示。
    /// 默认实现不可用，会抛出异常提示需要先配置 AI 服务。
    /// </summary>
    /// <param name="texts">文本集合</param>
    /// <param name="serviceId">指定服务ID（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>向量列表</returns>
    public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Embedding服务不可用，请先配置AI服务");
    }

    /// <summary>
    /// 计算两条向量的相似度（余弦相似度）。
    /// </summary>
    /// <param name="embedding1">向量1</param>
    /// <param name="embedding2">向量2</param>
    /// <returns>相似度（0-1）</returns>
    public double ComputeSimilarity(float[] embedding1, float[] embedding2)
    {
        if (embedding1 == null || embedding2 == null)
            return 0;

        if (embedding1.Length != embedding2.Length)
            throw new ArgumentException("向量维度不一致");

        if (embedding1.Length == 0)
            return 0;

        double dotProduct = 0;
        double magnitude1 = 0;
        double magnitude2 = 0;

        for (var i = 0; i < embedding1.Length; i++)
        {
            dotProduct += embedding1[i] * embedding2[i];
            magnitude1 += embedding1[i] * embedding1[i];
            magnitude2 += embedding2[i] * embedding2[i];
        }

        magnitude1 = Math.Sqrt(magnitude1);
        magnitude2 = Math.Sqrt(magnitude2);

        if (magnitude1 == 0 || magnitude2 == 0)
            return 0;

        return dotProduct / (magnitude1 * magnitude2);
    }
}
