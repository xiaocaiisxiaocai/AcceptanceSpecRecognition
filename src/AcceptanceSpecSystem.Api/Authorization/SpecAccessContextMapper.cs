using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application.Services;

namespace AcceptanceSpecSystem.Api.Authorization;

internal static class SpecAccessContextMapper
{
    public static SpecAccessContext ToAccessContext(this DataScopeResult scope)
    {
        return new SpecAccessContext
        {
            UserId = scope.UserId,
            CompanyId = scope.CompanyId,
            OrgUnitId = scope.OrgUnitId,
            IsAll = scope.IsAll,
            IncludeSelf = scope.IncludeSelf,
            OrgUnitIds = scope.OrgUnitIds
        };
    }
}
