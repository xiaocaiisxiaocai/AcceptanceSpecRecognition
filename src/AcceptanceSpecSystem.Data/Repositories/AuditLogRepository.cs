using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 审计日志仓储实现
/// </summary>
public class AuditLogRepository : Repository<AuditLog>, IAuditLogRepository
{
    public const int MaxPageSize = 200;

    /// <summary>
    /// 创建审计日志仓储实例
    /// </summary>
    public AuditLogRepository(AppDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 分页查询审计日志
    /// </summary>
    public async Task<(IReadOnlyList<AuditLog> Items, int Total)> GetPagedAsync(
        int page,
        int pageSize,
        AuditLogSource? source = null,
        AuditLogLevel? level = null,
        string? username = null,
        string? requestMethod = null,
        string? keyword = null,
        DateTime? from = null,
        DateTime? to = null,
        int? minStatusCode = null,
        int? maxStatusCode = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _dbSet.AsNoTracking().AsQueryable();

        if (source.HasValue)
            query = query.Where(x => x.Source == source.Value);

        if (level.HasValue)
            query = query.Where(x => x.Level == level.Value);

        if (!string.IsNullOrWhiteSpace(username))
            query = query.Where(x => x.Username == username);

        if (!string.IsNullOrWhiteSpace(requestMethod))
            query = query.Where(x => x.RequestMethod == requestMethod);

        if (from.HasValue)
            query = query.Where(x => x.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(x => x.CreatedAt <= to.Value);

        if (minStatusCode.HasValue)
            query = query.Where(x => x.StatusCode >= minStatusCode.Value);

        if (maxStatusCode.HasValue)
            query = query.Where(x => x.StatusCode <= maxStatusCode.Value);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(x =>
                (x.RequestPath != null && x.RequestPath.Contains(keyword)) ||
                (x.FrontendRoute != null && x.FrontendRoute.Contains(keyword)) ||
                (x.EventType != null && x.EventType.Contains(keyword)) ||
                (x.Details != null && x.Details.Contains(keyword)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    /// <summary>
    /// 删除指定时间点之前的审计日志
    /// </summary>
    public async Task<int> DeleteBeforeAsync(
        DateTime beforeTime,
        CancellationToken cancellationToken = default,
        int batchSize = 1000)
    {
        batchSize = Math.Clamp(batchSize, 1, 1000);
        var expired = await _dbSet
            .Where(x => x.CreatedAt < beforeTime)
            .OrderBy(x => x.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
            return 0;

        _dbSet.RemoveRange(expired);
        await _context.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    public async Task<int> DeleteOverflowAsync(
        int maxRecordCount,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        maxRecordCount = Math.Max(1, maxRecordCount);
        batchSize = Math.Clamp(batchSize, 1, 1000);
        var total = await _dbSet.CountAsync(cancellationToken);
        if (total <= maxRecordCount)
            return 0;

        var overflow = await _dbSet
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Take(Math.Min(batchSize, total - maxRecordCount))
            .ToListAsync(cancellationToken);
        _dbSet.RemoveRange(overflow);
        await _context.SaveChangesAsync(cancellationToken);
        return overflow.Count;
    }

    /// <summary>
    /// 按时间范围删除审计日志
    /// </summary>
    public async Task<int> DeleteByRangeAsync(
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet.AsQueryable();

        if (from.HasValue)
            query = query.Where(x => x.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(x => x.CreatedAt <= to.Value);

        return await query.ExecuteDeleteAsync(cancellationToken);
    }
}
