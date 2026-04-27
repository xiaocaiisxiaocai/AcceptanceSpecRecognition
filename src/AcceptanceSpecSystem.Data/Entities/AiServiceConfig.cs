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
    /// 服务用途（LLM/Embedding）
    /// </summary>
    public AiServicePurpose Purpose { get; set; } = AiServicePurpose.Llm;

    /// <summary>
    /// 优先级（越小越优先）
    /// </summary>
    public int Priority { get; set; } = 0;

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
    /// 是否关闭思考模式（当前主要用于 Ollama LLM）
    /// </summary>
    public bool DisableThinking { get; set; }

    /// <summary>
    /// 是否禁用。禁用后不参与 AI/Embedding 服务选择。
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    /// 该服务对应的默认召回候选数（主要供 Embedding 服务使用）
    /// </summary>
    public int DefaultRecallTopK { get; set; } = 2;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 兼容历史脏数据时使用的有效用途。
    /// 新建/编辑仍要求单一用途，这里只负责读取侧归一化。
    /// </summary>
    public AiServicePurpose GetEffectivePurpose()
    {
        if (Purpose == AiServicePurpose.Llm || Purpose == AiServicePurpose.Embedding)
        {
            return Purpose;
        }

        var hasLlmModel = HasLlmModel();
        var hasEmbeddingModel = HasEmbeddingModel();
        if (hasLlmModel && hasEmbeddingModel)
        {
            return AiServicePurpose.Llm | AiServicePurpose.Embedding;
        }

        if (hasLlmModel && !hasEmbeddingModel)
        {
            return AiServicePurpose.Llm;
        }

        if (!hasLlmModel && hasEmbeddingModel)
        {
            return AiServicePurpose.Embedding;
        }

        return AiServicePurpose.Llm;
    }

    /// <summary>
    /// 是否存在可用的 LLM 模型。
    /// </summary>
    public bool HasLlmModel()
    {
        return !string.IsNullOrWhiteSpace(LlmModel);
    }

    /// <summary>
    /// 是否存在可用的 Embedding 模型。
    /// </summary>
    public bool HasEmbeddingModel()
    {
        return !string.IsNullOrWhiteSpace(EmbeddingModel);
    }

    /// <summary>
    /// 标记仍未被迁移拆分的历史双用途脏数据。
    /// </summary>
    public bool IsLegacyDualPurposeConfiguration()
    {
        return Purpose != AiServicePurpose.Llm &&
               Purpose != AiServicePurpose.Embedding &&
               HasLlmModel() &&
               HasEmbeddingModel();
    }
}
