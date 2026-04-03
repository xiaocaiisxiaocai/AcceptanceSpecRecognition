namespace AcceptanceSpecSystem.Api.Options;

/// <summary>
/// 匹配知识配置选项
/// </summary>
public sealed class MatchingKnowledgeOptions
{
    public const string SectionName = "MatchingKnowledge";

    /// <summary>
    /// 品牌/组织别名字典
    /// </summary>
    public Dictionary<string, string> EntityAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 单位别名字典
    /// </summary>
    public Dictionary<string, string> UnitAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 单位换算系数
    /// </summary>
    public Dictionary<string, decimal> UnitFactors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 字段别名字典
    /// </summary>
    public Dictionary<string, string> FieldAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 冲突词对
    /// </summary>
    public List<ConflictPairOption> ConflictPairs { get; set; } = [];
}

/// <summary>
/// 冲突词对配置项
/// </summary>
public sealed class ConflictPairOption
{
    public string Left { get; set; } = string.Empty;

    public string Right { get; set; } = string.Empty;
}
