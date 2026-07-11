namespace AcceptanceSpecSystem.Api.Options;

public sealed class OrphanFileInspectionOptions
{
    public const string SectionName = "OrphanFileInspection";

    public bool Enabled { get; set; } = true;

    /// <summary>默认只记录候选，不执行删除。</summary>
    public bool ObservationMode { get; set; } = true;

    public int InitialDelaySeconds { get; set; } = 60;

    public int InspectionIntervalMinutes { get; set; } = 60;

    /// <summary>必须大于最长业务引用落盘窗口；默认七天。</summary>
    public int GracePeriodHours { get; set; } = 168;
}
