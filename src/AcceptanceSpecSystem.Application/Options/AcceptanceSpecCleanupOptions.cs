namespace AcceptanceSpecSystem.Application.Options;

public sealed class AcceptanceSpecCleanupOptions
{
    public const string SectionName = "AcceptanceSpecCleanup";

    public int BatchSize { get; set; } = 100;
    public int QuarantineDays { get; set; } = 30;
    public int ScanRetentionDays { get; set; } = 30;
    public int PollIntervalMilliseconds { get; set; } = 500;
}
