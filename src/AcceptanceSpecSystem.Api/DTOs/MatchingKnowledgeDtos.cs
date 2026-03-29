using System.ComponentModel.DataAnnotations;
using AcceptanceSpecSystem.Api.Options;

namespace AcceptanceSpecSystem.Api.DTOs;

/// <summary>
/// 匹配知识分层数据。
/// </summary>
public sealed class MatchingKnowledgeLayerDto
{
    /// <summary>
    /// 实体别名映射。
    /// </summary>
    public Dictionary<string, string> EntityAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 单位别名映射。
    /// </summary>
    public Dictionary<string, string> UnitAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 单位换算映射。
    /// </summary>
    public Dictionary<string, decimal> UnitFactors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 字段别名映射。
    /// </summary>
    public Dictionary<string, string> FieldAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 冲突词对。
    /// </summary>
    public List<ConflictPairDto> ConflictPairs { get; set; } = [];
}

/// <summary>
/// 匹配知识配置响应 DTO。
/// </summary>
public sealed class MatchingKnowledgeViewDto
{
    /// <summary>
    /// 系统内置规则。
    /// </summary>
    public MatchingKnowledgeLayerDto BuiltIn { get; set; } = new();

    /// <summary>
    /// 自定义扩展规则。
    /// </summary>
    public MatchingKnowledgeLayerDto Custom { get; set; } = new();

    /// <summary>
    /// 最终生效规则。
    /// </summary>
    public MatchingKnowledgeLayerDto Effective { get; set; } = new();
}

/// <summary>
/// 匹配知识保存请求，仅保存自定义扩展。
/// </summary>
public sealed class UpdateMatchingKnowledgeRequest
{
    /// <summary>
    /// 实体别名映射。
    /// </summary>
    [Required]
    public Dictionary<string, string> EntityAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 单位别名映射。
    /// </summary>
    [Required]
    public Dictionary<string, string> UnitAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 单位换算映射。
    /// </summary>
    [Required]
    public Dictionary<string, decimal> UnitFactors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 字段别名映射。
    /// </summary>
    [Required]
    public Dictionary<string, string> FieldAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 冲突词对。
    /// </summary>
    [Required]
    public List<ConflictPairDto> ConflictPairs { get; set; } = [];
}

/// <summary>
/// 冲突词对 DTO。
/// </summary>
public sealed class ConflictPairDto
{
    /// <summary>
    /// 左侧词。
    /// </summary>
    public string Left { get; set; } = string.Empty;

    /// <summary>
    /// 右侧词。
    /// </summary>
    public string Right { get; set; } = string.Empty;

    internal ConflictPairOption ToOption()
    {
        return new ConflictPairOption
        {
            Left = Left,
            Right = Right
        };
    }
}

/// <summary>
/// 匹配知识草稿生成请求。
/// </summary>
public sealed class GenerateMatchingKnowledgeDraftRequest
{
    /// <summary>
    /// 当前生成分类：entityAliases / unitAliases / fieldAliases / conflictPairs
    /// </summary>
    [Required]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 输入来源：text / documents
    /// </summary>
    [Required]
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// 粘贴文本输入。
    /// </summary>
    public string? InputText { get; set; }

    /// <summary>
    /// 已上传文档 ID 列表。
    /// </summary>
    public List<int> FileIds { get; set; } = [];

    /// <summary>
    /// 指定使用的 LLM 服务 ID；为空时按现有优先级选择。
    /// </summary>
    public int? LlmServiceId { get; set; }
}

/// <summary>
/// 匹配知识草稿响应。
/// </summary>
public sealed class MatchingKnowledgeDraftResponseDto
{
    /// <summary>
    /// 当前分类。
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 候选草稿列表。
    /// </summary>
    public List<MatchingKnowledgeDraftItemDto> Items { get; set; } = [];
}

/// <summary>
/// 匹配知识草稿候选项。
/// </summary>
public sealed class MatchingKnowledgeDraftItemDto
{
    /// <summary>
    /// 候选键。映射类表示别名/左侧词。
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 候选值。映射类表示标准值；冲突词对表示右侧词。
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 证据片段。
    /// </summary>
    public string EvidenceSnippet { get; set; } = string.Empty;

    /// <summary>
    /// 生成理由。
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 状态：ready / duplicate / conflict
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 状态说明。
    /// </summary>
    public string? StatusMessage { get; set; }
}
