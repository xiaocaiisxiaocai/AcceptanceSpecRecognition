using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 制程Repository接口
/// </summary>
public interface IProcessRepository : IRepository<Process>
{
    /// <summary>
    /// 获取制程及其验收规格数量
    /// </summary>
    /// <param name="id">制程ID</param>
    /// <returns>制程或null</returns>
    Task<Process?> GetWithSpecCountAsync(int id);
}
