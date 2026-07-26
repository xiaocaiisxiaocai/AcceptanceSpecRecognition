using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 组织节点Repository接口
/// </summary>
public interface IOrgUnitRepository : IRepository<OrgUnit>
{
    /// <summary>
    /// 获取指定公司下的根组织节点（单组织模式下唯一）。
    /// </summary>
    /// <param name="companyId">公司ID</param>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>根组织节点或 null</returns>
    Task<OrgUnit?> GetRootAsync(int companyId, CancellationToken cancellationToken = default);
}
