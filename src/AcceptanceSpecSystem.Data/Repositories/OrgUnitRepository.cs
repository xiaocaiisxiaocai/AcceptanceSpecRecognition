using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 组织节点Repository实现
/// </summary>
public class OrgUnitRepository : Repository<OrgUnit>, IOrgUnitRepository
{
    /// <summary>
    /// 创建OrgUnitRepository实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public OrgUnitRepository(AppDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 获取指定公司下的根组织节点（单组织模式下唯一）。
    /// </summary>
    /// <param name="companyId">公司ID</param>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>根组织节点或 null</returns>
    public async Task<OrgUnit?> GetRootAsync(int companyId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(org => org.CompanyId == companyId && org.ParentId == null && org.UnitType == OrgUnitType.Company)
            .OrderBy(org => org.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
