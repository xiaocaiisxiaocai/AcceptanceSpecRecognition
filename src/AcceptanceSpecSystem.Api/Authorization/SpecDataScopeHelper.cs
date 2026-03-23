using System.Security.Claims;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>
    /// 在数据库层面应用数据范围过滤，同时 Include 关联数据（EF Core 查询层，避免内存加载全部数据）
    /// </summary>
    public static IQueryable<AcceptanceSpec> ApplyScopeWithIncludes(
        IQueryable<AcceptanceSpec> query,
        DataScopeResult scope)
    {
        return ApplyScopeToQuery(query, scope)
            .Include(s => s.Customer)
            .Include(s => s.Process)
            .Include(s => s.MachineModel);
    }

    /// <summary>
    /// 在数据库层面应用数据范围过滤（EF Core 查询层）
    /// </summary>
    public static IQueryable<AcceptanceSpec> ApplyScopeToQuery(
        IQueryable<AcceptanceSpec> query,
        DataScopeResult scope)
    {
        if (scope.IsAll)
            return query;

        var scopedOrgUnitIds = scope.OrgUnitIds.Distinct().ToArray();

        if (scope.IncludeSelf && scopedOrgUnitIds.Length > 0)
        {
            return query.Where(s =>
                (s.CreatedByUserId.HasValue && s.CreatedByUserId.Value == scope.UserId) ||
                (s.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(s.OwnerOrgUnitId.Value)));
        }

        if (scope.IncludeSelf)
        {
            return query.Where(s =>
                s.CreatedByUserId.HasValue && s.CreatedByUserId.Value == scope.UserId);
        }

        if (scopedOrgUnitIds.Length > 0)
        {
            return query.Where(s =>
                s.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(s.OwnerOrgUnitId.Value));
        }

        return query.Where(_ => false);
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
