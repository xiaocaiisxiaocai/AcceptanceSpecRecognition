using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

public interface IDashboardAppService
{
    Task<DashboardSummaryDto?> GetSummaryAsync(
        int userId,
        int companyId,
        string? range,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 首页统计应用服务。
/// </summary>
public sealed class DashboardAppService : IDashboardAppService
{
    private const string SmartFillTaskType = "smart-fill";
    private readonly AppDbContext _dbContext;
    private readonly IAuthDataScopeService _authDataScopeService;

    public DashboardAppService(AppDbContext dbContext, IAuthDataScopeService authDataScopeService)
    {
        _dbContext = dbContext;
        _authDataScopeService = authDataScopeService;
    }

    public async Task<DashboardSummaryDto?> GetSummaryAsync(
        int userId,
        int companyId,
        string? range,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var scope = await _authDataScopeService.GetScopeAsync(userId, companyId, "spec");
        if (scope == null)
        {
            return null;
        }

        var period = ResolvePeriod(range, from, to);
        var scopedSpecs = ApplyScope(_dbContext.AcceptanceSpecs.AsNoTracking(), scope);

        var smartFillRecords = _dbContext.ExecutionHistoryRecords
            .AsNoTracking()
            .Where(record =>
                record.CompanyId == scope.CompanyId &&
                record.CreatedByUserId == scope.UserId &&
                record.TaskType == SmartFillTaskType &&
                record.DetailJson != string.Empty &&
                record.CreatedAt >= period.Start &&
                record.CreatedAt <= period.End);

        var customerTotal = await scopedSpecs
            .Select(spec => spec.CustomerId)
            .Distinct()
            .CountAsync(cancellationToken);
        var processTotal = await scopedSpecs
            .Where(spec => spec.ProcessId.HasValue)
            .Select(spec => spec.ProcessId!.Value)
            .Distinct()
            .CountAsync(cancellationToken);
        var specTotal = await scopedSpecs.CountAsync(cancellationToken);
        var importedSpecCount = await scopedSpecs
            .CountAsync(spec => spec.ImportedAt >= period.Start && spec.ImportedAt <= period.End, cancellationToken);

        var smartFillTaskCount = await smartFillRecords.CountAsync(cancellationToken);
        var smartFillTotalRows = await smartFillRecords.SumAsync(record => (int?)record.TotalRowCount, cancellationToken) ?? 0;
        // 首页“匹配度”只统计最终完整执行且已采用的行；预览候选、中途取消或卡住的任务不计入成功匹配。
        var smartFillMatchedRows = await smartFillRecords.SumAsync(record => (int?)record.AdoptedRowCount, cancellationToken) ?? 0;
        var smartFillAdoptedRows = await smartFillRecords.SumAsync(record => (int?)record.AdoptedRowCount, cancellationToken) ?? 0;

        return new DashboardSummaryDto
        {
            PeriodPreset = period.Preset,
            PeriodStart = period.Start,
            PeriodEnd = period.End,
            CustomerTotal = customerTotal,
            ProcessTotal = processTotal,
            SpecTotal = specTotal,
            ImportedSpecCount = importedSpecCount,
            SmartFillTaskCount = smartFillTaskCount,
            SmartFillTotalRows = smartFillTotalRows,
            SmartFillMatchedRows = smartFillMatchedRows,
            SmartFillAdoptedRows = smartFillAdoptedRows,
            MatchingRate = CalculateRate(smartFillMatchedRows, smartFillTotalRows),
            AdoptionRate = CalculateRate(smartFillAdoptedRows, smartFillTotalRows)
        };
    }

    private static DashboardPeriod ResolvePeriod(string? range, DateTime? from, DateTime? to)
    {
        var now = DateTime.UtcNow;
        var normalizedRange = string.IsNullOrWhiteSpace(range)
            ? "last7"
            : range.Trim().ToLowerInvariant();

        return normalizedRange switch
        {
            "last30" => new DashboardPeriod("last30", now.AddDays(-30), now),
            "custom" when from.HasValue && to.HasValue =>
                NormalizeCustomPeriod(from.Value, to.Value),
            _ => new DashboardPeriod("last7", now.AddDays(-7), now)
        };
    }

    private static DashboardPeriod NormalizeCustomPeriod(DateTime from, DateTime to)
    {
        var start = ToUtc(from);
        var end = ToUtc(to);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        return new DashboardPeriod("custom", start, end);
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, value.Kind == DateTimeKind.Unspecified ? DateTimeKind.Local : value.Kind)
                .ToUniversalTime();
    }

    private static double CalculateRate(int numerator, int denominator)
    {
        return denominator <= 0 ? 0 : Math.Round((double)numerator / denominator, 4);
    }

    private static IQueryable<AcceptanceSpec> ApplyScope(
        IQueryable<AcceptanceSpec> query,
        DataScopeResult scope)
    {
        if (scope.IsAll)
            return query;

        var orgUnitIds = scope.OrgUnitIds.Distinct().ToArray();
        if (scope.IncludeSelf && orgUnitIds.Length > 0)
        {
            return query.Where(spec =>
                spec.CreatedByUserId == scope.UserId ||
                (spec.OwnerOrgUnitId.HasValue && orgUnitIds.Contains(spec.OwnerOrgUnitId.Value)));
        }

        if (scope.IncludeSelf)
            return query.Where(spec => spec.CreatedByUserId == scope.UserId);

        if (orgUnitIds.Length > 0)
            return query.Where(spec => spec.OwnerOrgUnitId.HasValue && orgUnitIds.Contains(spec.OwnerOrgUnitId.Value));

        return query.Where(_ => false);
    }

    private sealed record DashboardPeriod(string Preset, DateTime Start, DateTime End);
}
