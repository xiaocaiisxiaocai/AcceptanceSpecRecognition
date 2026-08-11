namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 验收规格某个内容版本的不可变完整快照。
/// </summary>
public sealed class AcceptanceSpecContentVersion
{
    public long Id { get; set; }

    public int AcceptanceSpecId { get; set; }

    public long Version { get; set; }

    public string Project { get; set; } = string.Empty;

    public string Specification { get; set; } = string.Empty;

    public string? Acceptance { get; set; }

    public string? Remark { get; set; }

    public DateTime ChangedAtUtc { get; set; }

    public int? ChangedByUserId { get; set; }

    public string? ChangedByNameSnapshot { get; set; }

    public string ChangeSource { get; set; } = string.Empty;

    public string? ChangeReason { get; set; }

    public long? RestoredFromVersion { get; set; }

    public bool IsMigrationBaseline { get; set; }

    public AcceptanceSpec AcceptanceSpec { get; set; } = null!;
}
