using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 智能填充任务仓储实现
/// </summary>
public class MatchingFillTaskRepository : Repository<MatchingFillTask>, IMatchingFillTaskRepository
{
    public MatchingFillTaskRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<MatchingFillTask?> GetByTaskIdAsync(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            return null;

        return await _dbSet.FirstOrDefaultAsync(t => t.TaskId == taskId.Trim());
    }

    public async Task<int> DeleteBeforeAsync(DateTime beforeTime)
    {
        return await _dbSet
            .Where(t => t.CreatedAt < beforeTime)
            .ExecuteDeleteAsync();
    }
}
