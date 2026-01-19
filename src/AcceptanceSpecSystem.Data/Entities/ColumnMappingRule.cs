namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 导入列映射规则（全局）
/// 用于将 Word 表格的“表头文本”映射到系统目标字段（项目/规格/验收标准/备注）。
/// </summary>
public class ColumnMappingRule
{
    /// <summary>
    /// 主键ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 目标字段
    /// </summary>
    public ColumnMappingTargetField TargetField { get; set; }

    /// <summary>
    /// 匹配模式
    /// </summary>
    public ColumnMappingMatchMode MatchMode { get; set; } = ColumnMappingMatchMode.Contains;

    /// <summary>
    /// 匹配词（单条规则一条词）。例如：项目、项目管理、工艺流程
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// 优先级（越大越优先）
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// 列映射目标字段
/// </summary>
public enum ColumnMappingTargetField
{
    /// <summary>项目</summary>
    Project = 1,
    /// <summary>规格内容</summary>
    Specification = 2,
    /// <summary>验收标准</summary>
    Acceptance = 3,
    /// <summary>备注</summary>
    Remark = 4
}

/// <summary>
/// 列映射匹配模式
/// </summary>
public enum ColumnMappingMatchMode
{
    /// <summary>包含（默认）</summary>
    Contains = 1,
    /// <summary>完全相等</summary>
    Equals = 2,
    /// <summary>正则表达式</summary>
    Regex = 3
}

