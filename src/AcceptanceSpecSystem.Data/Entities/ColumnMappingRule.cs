namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// Word 表格列映射规则。
/// 仅用于根据表头自动预填项目/规格/验收/备注列，不参与 AI 匹配主链。
/// </summary>
public class ColumnMappingRule
{
    public const string GlobalScopeKey = "global";

    public int Id { get; set; }

    public ColumnMappingTargetField TargetField { get; set; }

    public ColumnMappingMatchMode MatchMode { get; set; } = ColumnMappingMatchMode.Equals;

    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// 可持久化的规则范围键。全局规则固定为 global，客户规则为 customer:{id}。
    /// 避免 CustomerId 为 null 时数据库唯一索引允许重复值。
    /// </summary>
    public string ScopeKey { get; set; } = GlobalScopeKey;

    /// <summary>
    /// 用于唯一约束的规范化匹配词。
    /// </summary>
    public string NormalizedPattern { get; set; } = string.Empty;

    /// <summary>
    /// 全局规则的规范化匹配词；客户规则保持 null。
    /// 单独的唯一索引保证同一表头在全局范围内只能归属一个目标字段。
    /// </summary>
    public string? GlobalNormalizedPatternKey { get; set; }

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

    public void RefreshUniqueIdentity()
    {
        ScopeKey = BuildScopeKey(CustomerId);
        NormalizedPattern = NormalizePattern(Pattern);
        GlobalNormalizedPatternKey = CustomerId.HasValue ? null : NormalizedPattern;
    }

    public static string BuildScopeKey(int? customerId) =>
        customerId.HasValue ? $"customer:{customerId.Value}" : GlobalScopeKey;

    public static string NormalizePattern(string? pattern)
    {
        return (pattern ?? string.Empty).Trim().ToUpperInvariant();
    }
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
