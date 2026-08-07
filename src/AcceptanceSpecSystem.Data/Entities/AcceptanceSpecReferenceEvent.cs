namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 验收规格逐次引用记录。
/// </summary>
public sealed class AcceptanceSpecReferenceEvent
{
    public long Id { get; set; }

    public int AcceptanceSpecId { get; set; }

    /// <summary>
    /// 引用发生时对应的规格内容版本。
    /// </summary>
    public long ReferenceVersion { get; set; }

    /// <summary>
    /// 智能填充任务ID；迁移前基线记录为空。
    /// </summary>
    public string? TaskId { get; set; }

    /// <summary>
    /// 同一任务内采用同一规格的稳定序号；迁移前基线记录为空。
    /// </summary>
    public int? TaskOccurrenceIndex { get; set; }

    /// <summary>
    /// 该记录代表的引用次数。未来逐次记录固定为1，迁移前基线可大于1。
    /// </summary>
    public long OccurrenceCount { get; set; } = 1;

    /// <summary>
    /// 最终提交成功的UTC时间；迁移前基线记录为空。
    /// </summary>
    public DateTime? ReferencedAtUtc { get; set; }

    public AcceptanceSpec AcceptanceSpec { get; set; } = null!;
}
