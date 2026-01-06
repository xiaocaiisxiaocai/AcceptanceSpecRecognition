using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Core.Interfaces;

/// <summary>
/// 向量嵌入服务接口
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// 生成单个文本的向量
    /// </summary>
    Task<float[]> EmbedAsync(string text);
    
    /// <summary>
    /// 批量生成向量
    /// </summary>
    Task<List<float[]>> EmbedBatchAsync(List<string> texts);
    
    /// <summary>
    /// 计算余弦相似度
    /// </summary>
    float CosineSimilarity(float[] vec1, float[] vec2);
    
    /// <summary>
    /// 获取当前模型信息
    /// </summary>
    ModelInfo GetModelInfo();
    
    /// <summary>
    /// 切换模型
    /// </summary>
    void SetModel(string modelName);
}

/// <summary>
/// 模型信息
/// </summary>
public class ModelInfo
{
    public string Name { get; set; } = string.Empty;
    public int Dimension { get; set; }
    public string Provider { get; set; } = string.Empty;
}
