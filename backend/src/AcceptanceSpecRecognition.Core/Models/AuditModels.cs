namespace AcceptanceSpecRecognition.Core.Models;

/// <summary>
/// 用户操作
/// </summary>
public class UserAction
{
    public string Type { get; set; } = string.Empty;
    public MatchQuery Query { get; set; } = new();
    public MatchCandidate? SelectedResult { get; set; }
    public string? ModifiedValue { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 日志筛选条件
/// </summary>
public class LogFilter
{
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? ActionType { get; set; }
}

/// <summary>
/// 审计日志筛选条件
/// </summary>
public class AuditLogFilter
{
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? ActionType { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>
/// 审计日志
/// </summary>
public class AuditLog
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public object? Data { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 审计日志条目
/// </summary>
public class AuditLogEntry
{
    public string Id { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? Details { get; set; }
    public string? RecordId { get; set; }
}

/// <summary>
/// 查询日志条目
/// </summary>
public class QueryLogEntry
{
    public string QueryText { get; set; } = string.Empty;
    public int ResultCount { get; set; }
    public float TopScore { get; set; }
    public string Confidence { get; set; } = string.Empty;

    /// <summary>
    /// 匹配模式：Embedding / LLM+Embedding
    /// </summary>
    public string MatchMode { get; set; } = "Embedding";

    /// <summary>
    /// 匹配耗时（毫秒）
    /// </summary>
    public long DurationMs { get; set; }
}

/// <summary>
/// 用户操作日志条目
/// </summary>
public class UserActionLogEntry
{
    public string Action { get; set; } = string.Empty;
    public string? RecordId { get; set; }
    public string? Details { get; set; }
}

/// <summary>
/// 配置修改日志条目
/// </summary>
public class ConfigChangeLogEntry
{
    public string ConfigSection { get; set; } = string.Empty;
    public string Changes { get; set; } = string.Empty;
}

/// <summary>
/// 审计查询结果
/// </summary>
public class AuditQueryResult
{
    public List<AuditLogEntry> Entries { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>
/// 审计日志存储
/// </summary>
public class AuditLogStore
{
    public List<AuditLogEntry> Entries { get; set; } = new();
    public DateTime UpdatedAt { get; set; }
}
