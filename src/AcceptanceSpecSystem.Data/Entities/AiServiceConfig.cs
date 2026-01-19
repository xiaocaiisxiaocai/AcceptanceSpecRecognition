namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// AI服务配置实体
/// </summary>
public class AiServiceConfig
{
    /// <summary>
    /// 配置ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 配置名称（唯一）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// AI服务类型
    /// </summary>
    public AiServiceType ServiceType { get; set; }

    /// <summary>
    /// API密钥（加密存储）
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// 服务端点URL
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Embedding模型名称
    /// </summary>
    public string? EmbeddingModel { get; set; }

    /// <summary>
    /// LLM模型名称
    /// </summary>
    public string? LlmModel { get; set; }

    /// <summary>
    /// 是否为默认配置
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
