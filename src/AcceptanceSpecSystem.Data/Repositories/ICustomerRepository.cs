using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 客户Repository接口
/// </summary>
public interface ICustomerRepository : IRepository<Customer>
{
    /// <summary>
    /// 根据名称获取客户
    /// </summary>
    /// <param name="name">客户名称</param>
    /// <returns>客户或null</returns>
    Task<Customer?> GetByNameAsync(string name);

    /// <summary>
    /// 获取客户及其所有验收规格
    /// </summary>
    /// <param name="id">客户ID</param>
    /// <returns>客户（包含验收规格）或null</returns>
    Task<Customer?> GetWithAcceptanceSpecsAsync(int id);

    /// <summary>
    /// 获取所有客户及其验收规格（用于统计）
    /// </summary>
    /// <returns>客户列表</returns>
    Task<IReadOnlyList<Customer>> GetAllWithAcceptanceSpecsAsync();
}
