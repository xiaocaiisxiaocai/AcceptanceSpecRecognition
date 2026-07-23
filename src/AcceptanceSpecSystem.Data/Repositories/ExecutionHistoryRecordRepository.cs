using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 执行记录仓储实现
/// </summary>
public class ExecutionHistoryRecordRepository : Repository<ExecutionHistoryRecord>, IExecutionHistoryRecordRepository
{
    public const int MaxPageSize = 200;

    public ExecutionHistoryRecordRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<(IReadOnlyList<ExecutionHistoryRecord> Items, int Total)> GetPagedOwnedAsync(
        int companyId,
        int userId,
        int page,
        int pageSize,
        string? keyword = null,
        string? taskType = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _dbSet.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.CreatedByUserId == userId);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(item =>
                item.TaskId.Contains(keyword) ||
                item.SourceFileName.Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(taskType))
        {
            query = query.Where(item => item.TaskType == taskType);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public Task<ExecutionHistoryRecord?> GetOwnedByIdAsync(int id, int companyId, int userId)
    {
        return _dbSet.AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.Id == id &&
                item.CompanyId == companyId &&
                item.CreatedByUserId == userId);
    }

    public Task<ExecutionHistoryRecord?> GetOwnedByTaskIdAsync(string taskId, int companyId, int userId)
    {
        return _dbSet
            .FirstOrDefaultAsync(item =>
                item.TaskId == taskId &&
                item.CompanyId == companyId &&
                item.CreatedByUserId == userId);
    }

    public async Task<int> DeleteBeforeAsync(
        DateTime beforeTime,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        batchSize = Math.Clamp(batchSize, 1, 1000);
        var expired = await _dbSet
            .Where(item => item.CreatedAt < beforeTime)
            .OrderBy(item => item.Id)
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
}
