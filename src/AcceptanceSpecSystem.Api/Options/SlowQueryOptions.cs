namespace AcceptanceSpecSystem.Api.Options;

/// <summary>
/// EF Core 慢查询日志配置。
/// </summary>
public sealed class SlowQueryOptions
{
    public const string SectionName = "SlowQuery";

    public bool Enabled { get; set; } = true;

    public int ThresholdMilliseconds { get; set; } = 500;

    public bool IncludeSqlText { get; set; } = false;
}
