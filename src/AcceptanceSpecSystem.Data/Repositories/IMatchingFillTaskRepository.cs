using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 智能填充任务仓储
/// </summary>
public interface IMatchingFillTaskRepository : IRepository<MatchingFillTask>
{
    /// <summary>
    /// 根据任务ID查询任务
    /// </summary>
    Task<MatchingFillTask?> GetByTaskIdAsync(string taskId);

    /// <summary>
    /// 删除指定时间之前的任务快照
    /// </summary>
    Task<int> DeleteBeforeAsync(DateTime beforeTime);
}
