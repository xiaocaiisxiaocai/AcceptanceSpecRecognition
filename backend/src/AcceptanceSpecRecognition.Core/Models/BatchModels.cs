namespace AcceptanceSpecRecognition.Core.Models;

/// <summary>
/// 批量处理进度
/// </summary>
public class BatchProgress
{
    public string TaskId { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// 批量处理结果
/// </summary>
public class BatchResult
{
    public string TaskId { get; set; } = string.Empty;
    public List<MatchResult> Results { get; set; } = new();
    public BatchSummary Summary { get; set; } = new();
}

/// <summary>
/// 批量处理汇总
/// </summary>
public class BatchSummary
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int LowConfidenceCount { get; set; }
}


/// <summary>
/// 批量处理请求
/// </summary>
public class BatchRequest
{
    public List<MatchQuery> Queries { get; set; } = new();
    public string? TaskName { get; set; }
}
