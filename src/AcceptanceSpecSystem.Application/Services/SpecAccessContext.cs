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

    public IQueryable<AcceptanceSpec> ApplySpecScopeToQuery(IQueryable<AcceptanceSpec> query)
    {
        if (IsAll)
            return query;

        var scopedOrgUnitIds = OrgUnitIds.Distinct().ToArray();
        if (IncludeSelf && scopedOrgUnitIds.Length > 0)
        {
            return query.Where(spec =>
                (spec.CreatedByUserId.HasValue && spec.CreatedByUserId.Value == UserId) ||
                (spec.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(spec.OwnerOrgUnitId.Value)));
        }

        if (IncludeSelf)
            return query.Where(spec => spec.CreatedByUserId == UserId);

        if (scopedOrgUnitIds.Length > 0)
            return query.Where(spec => spec.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(spec.OwnerOrgUnitId.Value));

        return query.Where(_ => false);
    }

    public IQueryable<WordFile> ApplyWordFileScopeToQuery(IQueryable<WordFile> query)
    {
        query = query.Where(file => !file.CompanyId.HasValue || file.CompanyId.Value == CompanyId);
        if (IsAll)
            return query;

        var selfQuery = query.Where(file => file.CreatedByUserId == UserId);
        var scopedOrgUnitIds = OrgUnitIds.Distinct().ToArray();
        return scopedOrgUnitIds.Length > 0
            ? selfQuery.Union(query.Where(file => file.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(file.OwnerOrgUnitId.Value)))
            : selfQuery;
    }

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

    public bool CanAccess(WordFile file)
    {
        if (file.CompanyId.HasValue && file.CompanyId.Value != CompanyId)
            return false;

        return IsAll || file.CreatedByUserId == UserId ||
               (file.OwnerOrgUnitId.HasValue && OrgUnitIds.Contains(file.OwnerOrgUnitId.Value));
    }
}
