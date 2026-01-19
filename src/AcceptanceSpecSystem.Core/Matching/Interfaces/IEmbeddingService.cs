namespace AcceptanceSpecSystem.Core.Matching.Interfaces;

/// <summary>
/// Embedding服务接口
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// 是否可用（已配置AI服务）
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 生成文本的Embedding向量
    /// </summary>
    /// <param name="text">输入文本</param>
    /// <returns>Embedding向量</returns>
    Task<float[]> GenerateEmbeddingAsync(string text);

    /// <summary>
    /// 批量生成Embedding向量
    /// </summary>
    /// <param name="texts">输入文本列表</param>
    /// <returns>Embedding向量列表</returns>
    Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts);

    /// <summary>
    /// 计算两个向量的余弦相似度
    /// </summary>
    /// <param name="embedding1">向量1</param>
    /// <param name="embedding2">向量2</param>
    /// <returns>相似度（0-1）</returns>
    double ComputeSimilarity(float[] embedding1, float[] embedding2);
}
