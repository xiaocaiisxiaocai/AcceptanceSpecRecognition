namespace AcceptanceSpecRecognition.Core.Models;

/// <summary>
/// 历史记录模型
/// </summary>
public class HistoryRecord
{
    public string Id { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string TechnicalSpec { get; set; } = string.Empty;
    public string ActualSpec { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public float[]? Embedding { get; set; }
}

/// <summary>
/// 历史记录存储结构
/// </summary>
public class HistoryRecordStore
{
    public List<HistoryRecord> Records { get; set; } = new();
}
