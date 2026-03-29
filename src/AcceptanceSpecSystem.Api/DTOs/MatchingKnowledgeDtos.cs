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
