using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 执行记录仓储接口
/// </summary>
public interface IExecutionHistoryRecordRepository : IRepository<ExecutionHistoryRecord>
{
    /// <summary>
    /// 按用户与公司分页查询执行记录
    /// </summary>
    Task<(IReadOnlyList<ExecutionHistoryRecord> Items, int Total)> GetPagedOwnedAsync(
        int companyId,
        int userId,
        int page,
        int pageSize,
        string? keyword = null,
        string? taskType = null);

    /// <summary>
    /// 获取归属于指定用户与公司的执行记录
    /// </summary>
    Task<ExecutionHistoryRecord?> GetOwnedByIdAsync(int id, int companyId, int userId);

    /// <summary>
    /// 按任务ID获取归属于指定用户与公司的执行记录
    /// </summary>
    Task<ExecutionHistoryRecord?> GetOwnedByTaskIdAsync(string taskId, int companyId, int userId);
}
