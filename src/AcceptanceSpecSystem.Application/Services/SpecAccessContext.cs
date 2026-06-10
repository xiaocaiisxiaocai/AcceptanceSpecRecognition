using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 验规相关数据范围上下文。
/// </summary>
public sealed class SpecAccessContext
{
    public int UserId { get; init; }

    public int CompanyId { get; init; }

    public int? OrgUnitId { get; init; }

    public bool IsAll { get; init; }

    public bool IncludeSelf { get; init; }

    public IReadOnlyCollection<int> OrgUnitIds { get; init; } = [];

    public bool CanAccess(AcceptanceSpec spec)
    {
        if (IsAll)
            return true;

        if (IncludeSelf &&
            spec.CreatedByUserId.HasValue &&
            spec.CreatedByUserId.Value == UserId)
        {
            return true;
        }

        return spec.OwnerOrgUnitId.HasValue && OrgUnitIds.Contains(spec.OwnerOrgUnitId.Value);
    }
}
