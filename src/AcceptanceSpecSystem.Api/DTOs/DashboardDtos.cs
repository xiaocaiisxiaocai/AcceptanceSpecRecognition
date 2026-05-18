namespace AcceptanceSpecSystem.Api.DTOs;

/// <summary>
/// 首页统计摘要。
/// </summary>
public sealed class DashboardSummaryDto
{
    public string PeriodPreset { get; set; } = "last7";

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public int CustomerTotal { get; set; }

    public int ProcessTotal { get; set; }

    public int SpecTotal { get; set; }

    public int ImportedSpecCount { get; set; }

    public int SmartFillTaskCount { get; set; }

    public int SmartFillTotalRows { get; set; }

    public int SmartFillMatchedRows { get; set; }

    public int SmartFillAdoptedRows { get; set; }

    public double MatchingRate { get; set; }

    public double AdoptionRate { get; set; }
}
