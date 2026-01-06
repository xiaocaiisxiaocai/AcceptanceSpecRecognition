namespace AcceptanceSpecRecognition.Core.Models;

/// <summary>
/// 用户反馈
/// </summary>
public class UserFeedback
{
    public string Id { get; set; } = string.Empty;
    public string QueryText { get; set; } = string.Empty;
    public string? SelectedSpec { get; set; }
    public string? CorrectedSpec { get; set; }
    public FeedbackType FeedbackType { get; set; }
    public string? Comment { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 反馈类型
/// </summary>
public enum FeedbackType
{
    Confirm,    // 确认匹配正确
    Correction, // 修正匹配结果
    Reject      // 拒绝所有匹配
}

/// <summary>
/// 反馈存储
/// </summary>
public class FeedbackStore
{
    public List<UserFeedback> Feedbacks { get; set; } = new();
}

/// <summary>
/// 同义词建议
/// </summary>
public class SynonymSuggestion
{
    public string SourceTerm { get; set; } = string.Empty;
    public string SuggestedSynonym { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public int OccurrenceCount { get; set; }
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// 反馈统计
/// </summary>
public class FeedbackStatistics
{
    public int TotalFeedbacks { get; set; }
    public int ConfirmCount { get; set; }
    public int CorrectionCount { get; set; }
    public int RejectCount { get; set; }
    public float ConfirmRate { get; set; }
    public DateTime? LastFeedbackTime { get; set; }
    public List<DailyFeedbackStat> DailyStats { get; set; } = new();
}

/// <summary>
/// 每日反馈统计
/// </summary>
public class DailyFeedbackStat
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
    public int ConfirmCount { get; set; }
    public int CorrectionCount { get; set; }
}
