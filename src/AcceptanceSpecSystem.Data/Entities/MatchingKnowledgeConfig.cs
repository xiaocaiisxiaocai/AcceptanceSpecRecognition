namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 匹配知识配置实体（单例模式，仅保留一条当前生效记录）。
/// </summary>
public class MatchingKnowledgeConfig
{
    /// <summary>
    /// 配置ID。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 实体别名 JSON。
    /// </summary>
    public string EntityAliasesJson { get; set; } = "{}";

    /// <summary>
    /// 单位别名 JSON。
    /// </summary>
    public string UnitAliasesJson { get; set; } = "{}";

    /// <summary>
    /// 单位换算 JSON。
    /// </summary>
    public string UnitFactorsJson { get; set; } = "{}";

    /// <summary>
    /// 字段别名 JSON。
    /// </summary>
    public string FieldAliasesJson { get; set; } = "{}";

    /// <summary>
    /// 冲突词对 JSON。
    /// </summary>
    public string ConflictPairsJson { get; set; } = "[]";

    /// <summary>
    /// 更新时间。
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
