using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Application.Options;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Application.Services;

public interface IDashboardAppService
{
    Task<DashboardSummaryDto?> GetSummaryAsync(
        int userId,
        int companyId,
        bool isAdmin,
        int? orgUnitId,
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
    private const int MaximumTrendDays = 366;
    private readonly AppDbContext _dbContext;
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _businessTimeZone;

    public DashboardAppService(
        AppDbContext dbContext,
        IAuthDataScopeService authDataScopeService,
        TimeProvider timeProvider,
        IOptions<DashboardOptions> options)
    {
        _dbContext = dbContext;
        _authDataScopeService = authDataScopeService;
        _timeProvider = timeProvider;
        _businessTimeZone = ResolveTimeZone(options.Value.TimeZoneId);
    }

    public async Task<DashboardSummaryDto?> GetSummaryAsync(
        int userId,
        int companyId,
        bool isAdmin,
        int? orgUnitId,
        string? range,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveDashboardScopeAsync(
            userId,
            companyId,
            isAdmin,
            orgUnitId,
            cancellationToken);
        if (scope == null)
        {
            return null;
        }

        var period = ResolvePeriod(range, from, to);
        var scopedSpecs = ApplyScope(
            _dbContext.AcceptanceSpecs
                .AsNoTracking()
                .Where(spec => spec.WordFile.CompanyId == scope.CompanyId),
            scope);

        var scopedHistoryRecords = ApplyHistoryScope(
            _dbContext.ExecutionHistoryRecords
                .AsNoTracking()
                .Where(record => record.CompanyId == scope.CompanyId),
            scope);
        var smartFillRecords = scopedHistoryRecords
            .AsNoTracking()
            .Where(record =>
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
        var offsetMinutes = (int)_businessTimeZone.GetUtcOffset(period.End).TotalMinutes;
        var importedByDay = await scopedSpecs
            .Where(spec => spec.ImportedAt >= period.Start && spec.ImportedAt <= period.End)
            .GroupBy(spec => spec.ImportedAt.AddMinutes(offsetMinutes).Date)
            .Select(group => new { Date = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => DateOnly.FromDateTime(item.Date), item => item.Count, cancellationToken);
        var smartFillByDay = await smartFillRecords
            .GroupBy(record => record.CreatedAt.AddMinutes(offsetMinutes).Date)
            .Select(group => new { Date = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => DateOnly.FromDateTime(item.Date), item => item.Count, cancellationToken);
        var dailyTrend = BuildDailyTrend(period, importedByDay, smartFillByDay);
        var recentExecutions = await scopedHistoryRecords
            .OrderByDescending(record => record.CreatedAt)
            .ThenByDescending(record => record.Id)
            .Take(5)
            .Select(record => new DashboardRecentExecutionDto
            {
                Id = record.Id,
                TaskId = record.TaskId,
                TaskType = record.TaskType,
                SourceFileName = record.SourceFileName,
                TotalRowCount = record.TotalRowCount,
                AdoptedRowCount = record.AdoptedRowCount,
                CreatedAt = record.CreatedAt
            })
            .ToListAsync(cancellationToken);

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
            AdoptionRate = CalculateRate(smartFillAdoptedRows, smartFillTotalRows),
            DailyTrend = dailyTrend,
            RecentExecutions = recentExecutions
        };
    }

    private async Task<DataScopeResult?> ResolveDashboardScopeAsync(
        int userId,
        int companyId,
        bool isAdmin,
        int? requestedOrgUnitId,
        CancellationToken cancellationToken)
    {
        if (!isAdmin)
        {
            if (requestedOrgUnitId.HasValue)
                throw new ApplicationServiceException(403, "普通用户只能查看所属部门的仪表盘");

            return await _authDataScopeService.GetScopeAsync(
                userId,
                companyId,
                "spec",
                cancellationToken);
        }

        if (!requestedOrgUnitId.HasValue)
        {
            return new DataScopeResult
            {
                UserId = userId,
                CompanyId = companyId,
                IsAll = true,
                IncludeSelf = true
            };
        }

        var selectedOrg = await _dbContext.OrgUnits
            .AsNoTracking()
            .Where(org =>
                org.Id == requestedOrgUnitId.Value &&
                org.CompanyId == companyId &&
                org.IsActive)
            .Select(org => new { org.Id, org.Path })
            .FirstOrDefaultAsync(cancellationToken);
        if (selectedOrg == null)
            throw new ApplicationServiceException(400, "所选部门不存在、已停用或不属于当前公司");

        var orgUnitIds = await _dbContext.OrgUnits
            .AsNoTracking()
            .Where(org =>
                org.CompanyId == companyId &&
                org.IsActive &&
                org.Path.StartsWith(selectedOrg.Path))
            .Select(org => org.Id)
            .ToListAsync(cancellationToken);

        return new DataScopeResult
        {
            UserId = userId,
            CompanyId = companyId,
            OrgUnitId = selectedOrg.Id,
            IsAll = false,
            IncludeSelf = false,
            OrgUnitIds = orgUnitIds
        };
    }

    private DashboardPeriod ResolvePeriod(string? range, DateTime? from, DateTime? to)
    {
        var now = _timeProvider.GetUtcNow();
        var localNow = TimeZoneInfo.ConvertTime(now, _businessTimeZone);
        var normalizedRange = string.IsNullOrWhiteSpace(range)
            ? "last7"
            : range.Trim().ToLowerInvariant();

        return normalizedRange switch
        {
            "last30" => CreateCalendarPeriod("last30", localNow.Date.AddDays(-29), now.UtcDateTime),
            "custom" when from.HasValue && to.HasValue =>
                NormalizeCustomPeriod(from.Value, to.Value),
            _ => CreateCalendarPeriod("last7", localNow.Date.AddDays(-6), now.UtcDateTime)
        };
    }

    private DashboardPeriod CreateCalendarPeriod(string preset, DateTime localStart, DateTime utcEnd)
    {
        var unspecifiedStart = DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified);
        var utcStart = TimeZoneInfo.ConvertTimeToUtc(unspecifiedStart, _businessTimeZone);
        return new DashboardPeriod(
            preset,
            utcStart,
            utcEnd,
            DateOnly.FromDateTime(localStart),
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcEnd, _businessTimeZone)));
    }

    private static IReadOnlyList<DashboardDailyTrendDto> BuildDailyTrend(
        DashboardPeriod period,
        IReadOnlyDictionary<DateOnly, int> importedByDay,
        IReadOnlyDictionary<DateOnly, int> smartFillByDay)
    {
        var start = period.StartDate;
        var end = period.EndDate;
        var result = new List<DashboardDailyTrendDto>(end.DayNumber - start.DayNumber + 1);

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            result.Add(new DashboardDailyTrendDto
            {
                Date = date,
                ImportedSpecCount = importedByDay.GetValueOrDefault(date),
                SmartFillTaskCount = smartFillByDay.GetValueOrDefault(date)
            });
        }

        return result;
    }

    private DashboardPeriod NormalizeCustomPeriod(DateTime from, DateTime to)
    {
        var start = ToUtc(from);
        var end = ToUtc(to);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        if ((end.Date - start.Date).TotalDays >= MaximumTrendDays)
            throw new ApplicationServiceException(400, $"仪表盘自定义周期不能超过 {MaximumTrendDays} 天");

        return new DashboardPeriod(
            "custom",
            start,
            end,
            ToBusinessDate(start),
            ToBusinessDate(end));
    }

    private DateOnly ToBusinessDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, _businessTimeZone));

    private static TimeZoneInfo ResolveTimeZone(string? configuredId)
    {
        var candidates = new[] { configuredId, "Asia/Shanghai", "China Standard Time", "UTC" }
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (DashboardTimeZoneResolver.TryResolveFixedOffset(candidate, out var zone))
                return zone;
        }

        return TimeZoneInfo.Utc;
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

    private IQueryable<ExecutionHistoryRecord> ApplyHistoryScope(
        IQueryable<ExecutionHistoryRecord> query,
        DataScopeResult scope)
    {
        if (scope.IsAll)
            return query;

        var orgUnitIds = scope.OrgUnitIds.Distinct().ToArray();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var scopedUserIds = _dbContext.AuthUserOrgUnits
            .AsNoTracking()
            .Where(link =>
                orgUnitIds.Contains(link.OrgUnitId) &&
                link.OrgUnit.CompanyId == scope.CompanyId &&
                link.OrgUnit.IsActive &&
                (!link.StartAt.HasValue || link.StartAt <= now) &&
                (!link.EndAt.HasValue || link.EndAt >= now))
            .Select(link => link.UserId)
            .Distinct();

        if (scope.IncludeSelf && orgUnitIds.Length > 0)
        {
            return query.Where(record =>
                (record.OwnerOrgUnitId.HasValue &&
                 orgUnitIds.Contains(record.OwnerOrgUnitId.Value)) ||
                (!record.OwnerOrgUnitId.HasValue &&
                 (record.CreatedByUserId == scope.UserId ||
                  (record.CreatedByUserId.HasValue &&
                   scopedUserIds.Contains(record.CreatedByUserId.Value)))));
        }

        if (scope.IncludeSelf)
            return query.Where(record => record.CreatedByUserId == scope.UserId);

        if (orgUnitIds.Length > 0)
        {
            return query.Where(record =>
                (record.OwnerOrgUnitId.HasValue &&
                 orgUnitIds.Contains(record.OwnerOrgUnitId.Value)) ||
                (!record.OwnerOrgUnitId.HasValue &&
                 record.CreatedByUserId.HasValue &&
                 scopedUserIds.Contains(record.CreatedByUserId.Value)));
        }

        return query.Where(_ => false);
    }

    private sealed record DashboardPeriod(
        string Preset,
        DateTime Start,
        DateTime End,
        DateOnly StartDate,
        DateOnly EndDate);
}

/// <summary>
/// 将配置的业务时区解析为固定偏移时区，避免 IANA 历史夏令时规则影响当前仪表盘统计。
/// </summary>
public static class DashboardTimeZoneResolver
{
    private const int DashboardWindowDays = 366;

    public static bool TryResolveFixedOffset(string? timeZoneId, out TimeZoneInfo timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }

        TimeZoneInfo sourceTimeZone;
        try
        {
            sourceTimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }

        var referenceDate = DateTime.UtcNow.Date;
        var fixedOffset = sourceTimeZone.GetUtcOffset(referenceDate);
        for (var dayOffset = -DashboardWindowDays; dayOffset <= DashboardWindowDays; dayOffset++)
        {
            if (sourceTimeZone.GetUtcOffset(referenceDate.AddDays(dayOffset)) != fixedOffset)
            {
                timeZone = TimeZoneInfo.Utc;
                return false;
            }
        }

        timeZone = TimeZoneInfo.CreateCustomTimeZone(
            sourceTimeZone.Id,
            fixedOffset,
            sourceTimeZone.DisplayName,
            sourceTimeZone.StandardName);
        return true;
    }
}
