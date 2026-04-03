using System.ComponentModel.DataAnnotations;
using AcceptanceSpecSystem.Api.Options;

namespace AcceptanceSpecSystem.Api.DTOs;

/// <summary>
/// 匹配知识分组 DTO。
/// </summary>
public sealed class MatchingKnowledgeGroupDto
{
    /// <summary>
    /// 组内词项；首项视为标准值。
    /// </summary>
    public List<string> Items { get; set; } = [];
}

/// <summary>
/// 匹配知识冲突分组 DTO。
/// </summary>
public sealed class MatchingKnowledgeConflictGroupDto
{
    /// <summary>
    /// 左冲突组词项。
    /// </summary>
    public List<string> LeftItems { get; set; } = [];

    /// <summary>
    /// 右冲突组词项。
    /// </summary>
    public List<string> RightItems { get; set; } = [];
}

/// <summary>
/// 匹配知识配置 DTO。
/// </summary>
public sealed class MatchingKnowledgeLayerDto
{
    /// <summary>
    /// 实体组。
    /// </summary>
    public List<MatchingKnowledgeGroupDto> EntityGroups { get; set; } = [];

    /// <summary>
    /// 单位组。
    /// </summary>
    public List<MatchingKnowledgeGroupDto> UnitGroups { get; set; } = [];

    /// <summary>
    /// 单位换算映射。
    /// </summary>
    public Dictionary<string, decimal> UnitFactors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 字段组。
    /// </summary>
    public List<MatchingKnowledgeGroupDto> FieldGroups { get; set; } = [];

    /// <summary>
    /// 冲突组。
    /// </summary>
    public List<MatchingKnowledgeConflictGroupDto> ConflictGroups { get; set; } = [];
}

/// <summary>
/// 匹配知识保存请求，保存当前完整配置。
/// </summary>
public sealed class UpdateMatchingKnowledgeRequest
{
    /// <summary>
    /// 实体组。
    /// </summary>
    [Required]
    public List<MatchingKnowledgeGroupDto> EntityGroups { get; set; } = [];

    /// <summary>
    /// 单位组。
    /// </summary>
    [Required]
    public List<MatchingKnowledgeGroupDto> UnitGroups { get; set; } = [];

    /// <summary>
    /// 单位换算映射。
    /// </summary>
    [Required]
    public Dictionary<string, decimal> UnitFactors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 字段组。
    /// </summary>
    [Required]
    public List<MatchingKnowledgeGroupDto> FieldGroups { get; set; } = [];

    /// <summary>
    /// 冲突组。
    /// </summary>
    [Required]
    public List<MatchingKnowledgeConflictGroupDto> ConflictGroups { get; set; } = [];
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
    /// 历史验规筛选条件。
    /// </summary>
    public MatchingKnowledgeDraftSpecFilterDto? SpecFilter { get; set; }

    /// <summary>
    /// 指定使用的 LLM 服务 ID；为空时按现有优先级选择。
    /// </summary>
    public int? LlmServiceId { get; set; }
}

/// <summary>
/// 匹配知识草稿生成时使用的历史验规筛选条件。
/// </summary>
public sealed class MatchingKnowledgeDraftSpecFilterDto
{
    /// <summary>
    /// 客户 ID。
    /// </summary>
    public int? CustomerId { get; set; }

    /// <summary>
    /// 制程 ID。
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// 机型 ID。
    /// </summary>
    public int? MachineModelId { get; set; }

    /// <summary>
    /// 关键词。
    /// </summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// 导入开始时间（含）。
    /// </summary>
    public DateTime? ImportedFrom { get; set; }

    /// <summary>
    /// 导入结束时间（含）。
    /// </summary>
    public DateTime? ImportedTo { get; set; }
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
