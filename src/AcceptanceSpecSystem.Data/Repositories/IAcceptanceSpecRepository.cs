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
    Task<IReadOnlyList<AcceptanceSpec>> GetAllWithCustomerAndProcessAsync();

    /// <summary>
    /// 获取单条验收规格（包含 Customer/Process/MachineModel 导航属性）
    /// </summary>
    Task<AcceptanceSpec?> GetByIdWithCustomerAndProcessAsync(int id);

    /// <summary>
    /// 根据制程ID获取所有验收规格
    /// </summary>
    /// <param name="processId">制程ID</param>
    /// <returns>验收规格列表</returns>
    Task<IReadOnlyList<AcceptanceSpec>> GetByProcessIdAsync(int processId);

    /// <summary>
    /// 根据Word文件ID获取所有验收规格
    /// </summary>
    /// <param name="wordFileId">Word文件ID</param>
    /// <returns>验收规格列表</returns>
    Task<IReadOnlyList<AcceptanceSpec>> GetByWordFileIdAsync(int wordFileId);

    /// <summary>
    /// 分页获取验收规格
    /// </summary>
    /// <param name="processId">制程ID</param>
    /// <param name="pageNumber">页码（从1开始）</param>
    /// <param name="pageSize">每页数量</param>
    /// <returns>验收规格列表</returns>
    Task<IReadOnlyList<AcceptanceSpec>> GetPagedAsync(int processId, int pageNumber, int pageSize);

    /// <summary>
    /// 获取验收规格及其来源文件信息
    /// </summary>
    /// <param name="id">验收规格ID</param>
    /// <returns>验收规格（包含Word文件）或null</returns>
    Task<AcceptanceSpec?> GetWithWordFileAsync(int id);

    /// <summary>
    /// 搜索验收规格
    /// </summary>
    /// <param name="processId">制程ID</param>
    /// <param name="searchTerm">搜索关键词</param>
    /// <returns>验收规格列表</returns>
    Task<IReadOnlyList<AcceptanceSpec>> SearchAsync(int processId, string searchTerm);

    /// <summary>
    /// 获取按（客户、机型、制程）分组的汇总信息，返回每组的名称和规格数量。
    /// 用途：左侧分组树构建。
    /// </summary>
    Task<IReadOnlyList<(int CustomerId, string CustomerName, int? MachineModelId, string? MachineModelName, int? ProcessId, string? ProcessName, int SpecCount)>> GetGroupSummaryAsync();
}
