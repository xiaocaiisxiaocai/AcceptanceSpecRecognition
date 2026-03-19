namespace AcceptanceSpecSystem.Data.Entities;

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

/// <summary>
/// 审计日志来源
/// </summary>
public enum AuditLogSource
{
    /// <summary>
    /// 后端请求日志
    /// </summary>
    BackendRequest = 0,

    /// <summary>
    /// 前端事件上报
    /// </summary>
    FrontendEvent = 1
}

/// <summary>
/// 审计日志级别
/// </summary>
public enum AuditLogLevel
{
    /// <summary>
    /// 信息
    /// </summary>
    Information = 0,

    /// <summary>
    /// 警告
    /// </summary>
    Warning = 1,

    /// <summary>
    /// 错误
    /// </summary>
    Error = 2
}

/// <summary>
/// 组织单元类型
/// </summary>
public enum OrgUnitType
{
    /// <summary>
    /// 公司
    /// </summary>
    Company = 0,

    /// <summary>
    /// 事业部
    /// </summary>
    Division = 1,

    /// <summary>
    /// 部门
    /// </summary>
    Department = 2,

    /// <summary>
    /// 课别
    /// </summary>
    Section = 3
}

/// <summary>
/// 权限类型
/// </summary>
public enum PermissionType
{
    /// <summary>
    /// 页面权限
    /// </summary>
    Page = 0,

    /// <summary>
    /// 按钮权限
    /// </summary>
    Button = 1,

    /// <summary>
    /// API 权限
    /// </summary>
    Api = 2
}

/// <summary>
/// 数据范围类型
/// </summary>
public enum DataScopeType
{
    /// <summary>
    /// 仅本人
    /// </summary>
    Self = 0,

    /// <summary>
    /// 单个组织节点
    /// </summary>
    OrgNode = 1,

    /// <summary>
    /// 组织节点及子树
    /// </summary>
    OrgSubtree = 2,

    /// <summary>
    /// 自定义多个组织节点
    /// </summary>
    CustomNodes = 3,

    /// <summary>
    /// 全量
    /// </summary>
    All = 4
}
