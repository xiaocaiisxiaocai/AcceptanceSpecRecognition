using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 验收规格Repository接口
/// </summary>
public interface IAcceptanceSpecRepository : IRepository<AcceptanceSpec>
{
    /// <summary>
    /// 获取所有验收规格（包含 Customer/Process/MachineModel 导航属性，用于列表展示名称）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyList<AcceptanceSpec>> GetAllWithCustomerAndProcessAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取单条验收规格（包含 Customer/Process/MachineModel 导航属性）
    /// </summary>
    /// <param name="id">验收规格ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<AcceptanceSpec?> GetByIdWithCustomerAndProcessAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据制程ID获取所有验收规格
    /// </summary>
    /// <param name="processId">制程ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验收规格列表</returns>
    Task<IReadOnlyList<AcceptanceSpec>> GetByProcessIdAsync(int processId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据Word文件ID获取所有验收规格
    /// </summary>
    /// <param name="wordFileId">Word文件ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验收规格列表</returns>
    Task<IReadOnlyList<AcceptanceSpec>> GetByWordFileIdAsync(int wordFileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页获取验收规格
    /// </summary>
    /// <param name="processId">制程ID</param>
    /// <param name="pageNumber">页码（从1开始）</param>
    /// <param name="pageSize">每页数量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验收规格列表</returns>
    Task<IReadOnlyList<AcceptanceSpec>> GetPagedAsync(int processId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取验收规格及其来源文件信息
    /// </summary>
    /// <param name="id">验收规格ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验收规格（包含Word文件）或null</returns>
    Task<AcceptanceSpec?> GetWithWordFileAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 搜索验收规格
    /// </summary>
    /// <param name="processId">制程ID</param>
    /// <param name="searchTerm">搜索关键词</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验收规格列表</returns>
    Task<IReadOnlyList<AcceptanceSpec>> SearchAsync(int processId, string searchTerm, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按筛选条件分页获取验收规格，并在数据库侧完成范围过滤、查询和分页。
    /// </summary>
    /// <param name="options">查询条件</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<(IReadOnlyList<AcceptanceSpec> Items, int Total)> GetPagedWithFilterAsync(
        AcceptanceSpecQueryOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按筛选条件获取验收规格，并在数据库侧完成范围过滤和查询。
    /// </summary>
    /// <param name="options">查询条件</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyList<AcceptanceSpec>> GetFilteredWithIncludesAsync(
        AcceptanceSpecQueryOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按筛选条件获取分组汇总，并在数据库侧完成范围过滤和分组。
    /// </summary>
    /// <param name="options">查询条件</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyList<AcceptanceSpecGroupSummaryItem>> GetGroupSummaryWithFilterAsync(
        AcceptanceSpecQueryOptions options,
        CancellationToken cancellationToken = default);
}
