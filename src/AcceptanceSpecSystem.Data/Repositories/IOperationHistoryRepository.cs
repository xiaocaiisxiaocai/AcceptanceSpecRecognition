using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 操作历史Repository接口
/// </summary>
public interface IOperationHistoryRepository : IRepository<OperationHistory>
{
    /// <summary>
    /// 获取最近的操作历史
    /// </summary>
    /// <param name="count">数量</param>
    /// <returns>操作历史列表</returns>
    Task<IReadOnlyList<OperationHistory>> GetRecentAsync(int count);

    /// <summary>
    /// 根据操作类型获取历史记录
    /// </summary>
    /// <param name="operationType">操作类型</param>
    /// <returns>操作历史列表</returns>
    Task<IReadOnlyList<OperationHistory>> GetByTypeAsync(OperationType operationType);

    /// <summary>
    /// 获取可撤销的操作历史
    /// </summary>
    /// <returns>可撤销的操作历史列表</returns>
    Task<IReadOnlyList<OperationHistory>> GetUndoableAsync();

    /// <summary>
    /// 分页获取操作历史
    /// </summary>
    /// <param name="pageNumber">页码（从1开始）</param>
    /// <param name="pageSize">每页数量</param>
    /// <returns>操作历史列表</returns>
    Task<IReadOnlyList<OperationHistory>> GetPagedAsync(int pageNumber, int pageSize);
}
