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
    /// 获取重复分析所需的有界轻量候选；调用方传入上限加一以识别整体超限。
    /// </summary>
    Task<IReadOnlyList<AcceptanceSpecDuplicateCandidate>> GetDuplicateCandidatesAsync(
        AcceptanceSpecQueryOptions options,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按筛选条件获取分组汇总，并在数据库侧完成范围过滤和分组。
    /// </summary>
    /// <param name="options">查询条件</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyList<AcceptanceSpecGroupSummaryItem>> GetGroupSummaryWithFilterAsync(
        AcceptanceSpecQueryOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在给定数据范围内，按客户ID分组统计每个客户拥有的制程数（Distinct ProcessId）。
    /// 收敛自原先各调用方各自拼接"范围过滤 + GroupBy + Count"的重复写法。
    /// </summary>
    /// <param name="scope">数据范围（仅使用其中的 UserId/IsAll/IncludeSelf/OrgUnitIds 字段）</param>
    /// <param name="customerIds">要统计的客户ID集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<Dictionary<int, int>> GetProcessCountByCustomerAsync(
        AcceptanceSpecQueryOptions scope,
        IReadOnlyCollection<int> customerIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在给定数据范围内，按客户ID分组统计验收规格数量（包含未关联制程的规格）。
    /// </summary>
    Task<Dictionary<int, int>> GetSpecCountByCustomerAsync(
        AcceptanceSpecQueryOptions scope,
        IReadOnlyCollection<int> customerIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在给定数据范围内，按制程ID分组统计每个制程下的验收规格数量。
    /// </summary>
    /// <param name="scope">数据范围（仅使用其中的 UserId/IsAll/IncludeSelf/OrgUnitIds 字段）</param>
    /// <param name="processIds">要统计的制程ID集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<Dictionary<int, int>> GetSpecCountByProcessAsync(
        AcceptanceSpecQueryOptions scope,
        IReadOnlyCollection<int> processIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在给定数据范围内，按机型ID分组统计每个机型下的验收规格数量。
    /// </summary>
    /// <param name="scope">数据范围（仅使用其中的 UserId/IsAll/IncludeSelf/OrgUnitIds 字段）</param>
    /// <param name="machineModelIds">要统计的机型ID集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<Dictionary<int, int>> GetSpecCountByMachineModelAsync(
        AcceptanceSpecQueryOptions scope,
        IReadOnlyCollection<int> machineModelIds,
        CancellationToken cancellationToken = default);
}
