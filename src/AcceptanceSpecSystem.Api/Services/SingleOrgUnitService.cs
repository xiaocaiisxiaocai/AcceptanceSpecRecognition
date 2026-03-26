using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 单组织系统辅助方法。
/// </summary>
public static class SingleOrgUnitService
{
    /// <summary>
    /// 获取公司根组织。
    /// </summary>
    public static Task<OrgUnit?> GetRootOrgUnitAsync(
        AppDbContext dbContext,
        int companyId,
        bool asNoTracking = true,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.OrgUnits
            .Where(org => org.CompanyId == companyId && org.ParentId == null && org.UnitType == OrgUnitType.Company);

        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return query
            .OrderBy(org => org.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// 获取公司根组织 ID。
    /// </summary>
    public static async Task<int?> GetRootOrgUnitIdAsync(
        AppDbContext dbContext,
        int companyId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.OrgUnits
            .AsNoTracking()
            .Where(org => org.CompanyId == companyId && org.ParentId == null && org.UnitType == OrgUnitType.Company)
            .OrderBy(org => org.Id)
            .Select(org => (int?)org.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
