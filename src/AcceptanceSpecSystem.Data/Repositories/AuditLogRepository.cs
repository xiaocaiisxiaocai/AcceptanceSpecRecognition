using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 审计日志仓储实现
/// </summary>
public class AuditLogRepository : Repository<AuditLog>, IAuditLogRepository
{
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
        int? maxStatusCode = null)
    {
        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 20;

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

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    /// <summary>
    /// 删除指定时间点之前的审计日志
    /// </summary>
    public async Task<int> DeleteBeforeAsync(DateTime beforeTime)
    {
        return await _dbSet
            .Where(x => x.CreatedAt < beforeTime)
            .ExecuteDeleteAsync();
    }

    /// <summary>
    /// 按时间范围删除审计日志
    /// </summary>
    public async Task<int> DeleteByRangeAsync(DateTime? from = null, DateTime? to = null)
    {
        var query = _dbSet.AsQueryable();

        if (from.HasValue)
            query = query.Where(x => x.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(x => x.CreatedAt <= to.Value);

        return await query.ExecuteDeleteAsync();
    }
}
