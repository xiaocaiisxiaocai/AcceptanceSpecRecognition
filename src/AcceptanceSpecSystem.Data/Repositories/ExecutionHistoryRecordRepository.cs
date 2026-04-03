using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 执行记录仓储实现
/// </summary>
public class ExecutionHistoryRecordRepository : Repository<ExecutionHistoryRecord>, IExecutionHistoryRecordRepository
{
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
        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 20;

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
}
