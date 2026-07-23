namespace AcceptanceSpecSystem.Api.Options;

public sealed class AuthRefreshSessionRetentionOptions
{
    public const string SectionName = "AuthRefreshSessionRetention";

    public bool Enabled { get; set; } = true;
    public int RetentionDaysAfterExpiry { get; set; } = 30;
    public int MaxRecordCount { get; set; } = 500_000;
    public int CleanupIntervalHours { get; set; } = 24;
    public int BatchSize { get; set; } = 500;
    public int MaxBatchesPerRun { get; set; } = 10;
}
