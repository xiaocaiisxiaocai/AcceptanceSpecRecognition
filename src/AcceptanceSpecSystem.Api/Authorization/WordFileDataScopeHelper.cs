using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.Authorization;

/// <summary>
/// 上传文件数据范围辅助。
/// </summary>
public static class WordFileDataScopeHelper
{
    public static IQueryable<WordFile> ApplyOwnershipScopeToQuery(
        IQueryable<WordFile> query,
        DataScopeResult scope)
    {
        query = query.Where(file => !file.CompanyId.HasValue || file.CompanyId.Value == scope.CompanyId);
        var selfQuery = query.Where(file =>
            file.CreatedByUserId.HasValue && file.CreatedByUserId.Value == scope.UserId);

        if (scope.IsAll)
        {
            return query;
        }

        var scopedOrgUnitIds = scope.OrgUnitIds.Distinct().ToArray();
        if (scopedOrgUnitIds.Length > 0)
        {
            return selfQuery.Union(query.Where(file =>
                file.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(file.OwnerOrgUnitId.Value)));
        }

        return selfQuery;
    }

    public static bool CanAccess(WordFile file, DataScopeResult scope)
    {
        if (file.CompanyId.HasValue && file.CompanyId.Value != scope.CompanyId)
        {
            return false;
        }

        if (scope.IsAll)
        {
            return true;
        }

        if (file.CreatedByUserId.HasValue &&
            file.CreatedByUserId.Value == scope.UserId)
        {
            return true;
        }

        if (file.OwnerOrgUnitId.HasValue &&
            scope.OrgUnitIds.Contains(file.OwnerOrgUnitId.Value))
        {
            return true;
        }

        return false;
    }
}
