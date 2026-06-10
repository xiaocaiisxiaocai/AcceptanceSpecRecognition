namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 用户多组织裁剪策略
/// </summary>
public static class AuthUserOrgUnitSingleOrgPolicy
{
    public static AuthUserOrgUnit? SelectOrgUnitToKeep(IEnumerable<AuthUserOrgUnit>? orgUnits)
    {
        if (orgUnits == null)
            return null;

        var orgUnitList = orgUnits.ToList();
        var primaryCount = orgUnitList.Count(orgUnit => orgUnit.IsPrimary);

        return orgUnitList
            .OrderBy(orgUnit => primaryCount == 1 && orgUnit.IsPrimary ? 0 : 1)
            .ThenBy(orgUnit => orgUnit.CreatedAt)
            .ThenBy(orgUnit => orgUnit.Id)
            .FirstOrDefault();
    }
}
