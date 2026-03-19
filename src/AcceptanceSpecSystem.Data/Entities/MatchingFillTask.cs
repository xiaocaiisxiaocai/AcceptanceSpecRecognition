namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 智能填充任务快照（用于下载与断点恢复）
/// </summary>
public class MatchingFillTask
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 任务ID（业务唯一键）
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// 源文件ID
    /// </summary>
    public int SourceFileId { get; set; }

    /// <summary>
    /// 任务快照 JSON（序列化 FillTaskResult）
    /// </summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 导航属性：源文件
    /// </summary>
    public WordFile? SourceFile { get; set; }
}
