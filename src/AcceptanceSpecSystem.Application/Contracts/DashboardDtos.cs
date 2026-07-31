namespace AcceptanceSpecSystem.Application.Contracts;

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
    public IReadOnlyList<DashboardDailyTrendDto> DailyTrend { get; set; } = [];
    public IReadOnlyList<DashboardRecentExecutionDto> RecentExecutions { get; set; } = [];
}

public sealed class DashboardDailyTrendDto
{
    public DateOnly Date { get; set; }
    public int ImportedSpecCount { get; set; }
    public int SmartFillTaskCount { get; set; }
}

public sealed class DashboardRecentExecutionDto
{
    public int Id { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public int TotalRowCount { get; set; }
    public int AdoptedRowCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
