using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 操作历史Repository实现
/// </summary>
public class OperationHistoryRepository : Repository<OperationHistory>, IOperationHistoryRepository
{
    /// <summary>
    /// 创建OperationHistoryRepository实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public OperationHistoryRepository(AppDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 获取最近的操作历史记录。
    /// </summary>
    /// <param name="count">数量</param>
    /// <returns>操作历史列表</returns>
    public async Task<IReadOnlyList<OperationHistory>> GetRecentAsync(int count)
    {
        return await _dbSet
            .OrderByDescending(h => h.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

    /// <summary>
    /// 获取指定类型的操作历史记录（按时间倒序）。
    /// </summary>
    /// <param name="operationType">操作类型</param>
    /// <returns>操作历史列表</returns>
    public async Task<IReadOnlyList<OperationHistory>> GetByTypeAsync(OperationType operationType)
    {
        return await _dbSet
            .Where(h => h.OperationType == operationType)
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// 获取可撤销的操作历史记录（按时间倒序）。
    /// </summary>
    /// <returns>可撤销的操作历史列表</returns>
    public async Task<IReadOnlyList<OperationHistory>> GetUndoableAsync()
    {
        return await _dbSet
            .Where(h => h.CanUndo)
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// 分页获取操作历史记录（按时间倒序）。
    /// </summary>
    /// <param name="pageNumber">页码（从1开始）</param>
    /// <param name="pageSize">每页数量</param>
    /// <returns>操作历史列表</returns>
    public async Task<IReadOnlyList<OperationHistory>> GetPagedAsync(int pageNumber, int pageSize)
    {
        return await _dbSet
            .OrderByDescending(h => h.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }
}
