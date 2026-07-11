using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

public static class SpecDataScopeRules
{
    public static IQueryable<AcceptanceSpec> ApplyScopeToQuery(
        IQueryable<AcceptanceSpec> query,
        DataScopeResult scope)
    {
        if (scope.IsAll) return query;
        var orgUnitIds = scope.OrgUnitIds.Distinct().ToArray();
        if (scope.IncludeSelf && orgUnitIds.Length > 0)
            return query.Where(spec => spec.CreatedByUserId == scope.UserId ||
                                       (spec.OwnerOrgUnitId.HasValue && orgUnitIds.Contains(spec.OwnerOrgUnitId.Value)));
        if (scope.IncludeSelf) return query.Where(spec => spec.CreatedByUserId == scope.UserId);
        if (orgUnitIds.Length > 0)
            return query.Where(spec => spec.OwnerOrgUnitId.HasValue && orgUnitIds.Contains(spec.OwnerOrgUnitId.Value));
        return query.Where(_ => false);
    }

    public static IReadOnlyList<AcceptanceSpec> ApplyScope(IEnumerable<AcceptanceSpec> specs, DataScopeResult scope)
    {
        var materialized = specs as IReadOnlyList<AcceptanceSpec> ?? specs.ToList();
        if (scope.IsAll) return materialized;
        var orgUnitIds = scope.OrgUnitIds.ToHashSet();
        return materialized.Where(spec => CanAccess(spec, scope, orgUnitIds)).ToList();
    }

    public static bool CanAccess(AcceptanceSpec spec, DataScopeResult scope) =>
        scope.IsAll || CanAccess(spec, scope, scope.OrgUnitIds.ToHashSet());

    private static bool CanAccess(AcceptanceSpec spec, DataScopeResult scope, HashSet<int> orgUnitIds) =>
        (scope.IncludeSelf && spec.CreatedByUserId == scope.UserId) ||
        (spec.OwnerOrgUnitId.HasValue && orgUnitIds.Contains(spec.OwnerOrgUnitId.Value));
}
