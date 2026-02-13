namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 操作类型枚举
/// </summary>
public enum OperationType
{
    /// <summary>
    /// 导入操作
    /// </summary>
    Import,

    /// <summary>
    /// 填充操作
    /// </summary>
    Fill,

    /// <summary>
    /// 删除操作
    /// </summary>
    Delete
}

/// <summary>
/// AI服务类型枚举
/// </summary>
public enum AiServiceType
{
    // 在线服务
    /// <summary>
    /// OpenAI API
    /// </summary>
    OpenAI,

    /// <summary>
    /// Azure OpenAI服务
    /// </summary>
    AzureOpenAI,

    // 本地私有化服务
    /// <summary>
    /// Ollama本地服务
    /// </summary>
    Ollama,

    /// <summary>
    /// LM Studio本地服务
    /// </summary>
    LMStudio,

    /// <summary>
    /// 自定义OpenAI兼容API
    /// </summary>
    CustomOpenAICompatible
}

/// <summary>
/// AI服务用途枚举（可组合）
/// </summary>
[Flags]
public enum AiServicePurpose
{
    /// <summary>
    /// 未指定
    /// </summary>
    None = 0,

    /// <summary>
    /// LLM 推理
    /// </summary>
    Llm = 1,

    /// <summary>
    /// 向量 Embedding
    /// </summary>
    Embedding = 2
}

/// <summary>
/// 简繁转换模式枚举
/// </summary>
public enum ChineseConversionMode
{
    /// <summary>
    /// 不转换
    /// </summary>
    None,

    /// <summary>
    /// 简体转台湾繁体
    /// </summary>
    HansToTW,

    /// <summary>
    /// 台湾繁体转简体
    /// </summary>
    TWToHans
}
