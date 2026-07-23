namespace AcceptanceSpecSystem.Api.Options;

/// <summary>
/// 执行历史的有界保留策略。
/// </summary>
public sealed class ExecutionHistoryRetentionOptions
{
    public const string SectionName = "ExecutionHistoryRetention";

    public bool Enabled { get; set; } = true;

    public int RetentionDays { get; set; } = 365;

    public int MaxRecordCount { get; set; } = 100_000;

    public int CleanupIntervalHours { get; set; } = 24;

    public int BatchSize { get; set; } = 500;

    public int MaxBatchesPerRun { get; set; } = 10;
}
