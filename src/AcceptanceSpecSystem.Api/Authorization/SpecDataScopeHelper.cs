using System.Security.Claims;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.Authorization;

/// <summary>
/// 验收规格数据范围辅助
/// </summary>
public static class SpecDataScopeHelper
{
    public static async Task<DataScopeResult?> ResolveScopeAsync(
        ClaimsPrincipal user,
        IAuthDataScopeService authDataScopeService)
    {
        var userId = AuthClaimHelper.GetUserId(user);
        var companyId = AuthClaimHelper.GetCompanyId(user);
        if (!userId.HasValue || !companyId.HasValue)
            return null;

        return await authDataScopeService.GetScopeAsync(userId.Value, companyId.Value, "spec");
    }

    public static IReadOnlyList<AcceptanceSpec> ApplyScope(
        IEnumerable<AcceptanceSpec> specs,
        DataScopeResult scope)
    {
        var materialized = specs as IReadOnlyList<AcceptanceSpec> ?? specs.ToList();
        if (scope.IsAll)
            return materialized;

        var scopedOrgUnitIds = scope.OrgUnitIds.ToHashSet();
        return materialized.Where(spec => CanAccess(spec, scope, scopedOrgUnitIds)).ToList();
    }

    public static bool CanAccess(AcceptanceSpec spec, DataScopeResult scope)
    {
        if (scope.IsAll)
            return true;

        return CanAccess(spec, scope, scope.OrgUnitIds.ToHashSet());
    }

    private static bool CanAccess(
        AcceptanceSpec spec,
        DataScopeResult scope,
        HashSet<int> scopedOrgUnitIds)
    {
        if (scope.IncludeSelf &&
            spec.CreatedByUserId.HasValue &&
            spec.CreatedByUserId.Value == scope.UserId)
        {
            return true;
        }

        if (spec.OwnerOrgUnitId.HasValue &&
            scopedOrgUnitIds.Contains(spec.OwnerOrgUnitId.Value))
        {
            return true;
        }

        return false;
    }
}
