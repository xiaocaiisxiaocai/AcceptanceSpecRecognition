namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// Word 表格列映射规则。
/// 仅用于根据表头自动预填项目/规格/验收/备注列，不参与 AI 匹配主链。
/// </summary>
public class ColumnMappingRule
{
    public int Id { get; set; }

    public ColumnMappingTargetField TargetField { get; set; }

    public ColumnMappingMatchMode MatchMode { get; set; } = ColumnMappingMatchMode.Equals;

    public string Pattern { get; set; } = string.Empty;

    public int Priority { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 规则来源。
    /// </summary>
    public ColumnMappingRuleSource Source { get; set; } = ColumnMappingRuleSource.Manual;

    /// <summary>
    /// 关联客户；null 表示全局规则。
    /// </summary>
    public int? CustomerId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// 列映射目标字段。
/// </summary>
public enum ColumnMappingTargetField
{
    Project = 1,
    Specification = 2,
    Acceptance = 3,
    Remark = 4
}

/// <summary>
/// 列映射匹配模式。
/// </summary>
public enum ColumnMappingMatchMode
{
    Contains = 1,
    Equals = 2,
    Regex = 3
}

/// <summary>
/// 列映射规则来源。
/// </summary>
public enum ColumnMappingRuleSource
{
    Builtin = 1,
    Manual = 2,
    Learned = 3
}
