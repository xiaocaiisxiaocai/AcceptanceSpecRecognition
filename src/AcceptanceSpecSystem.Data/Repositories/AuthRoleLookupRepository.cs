using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 角色只读查询仓储实现。
/// </summary>
public sealed class AuthRoleLookupRepository : IAuthRoleLookupRepository
{
    private readonly AppDbContext _context;

    public AuthRoleLookupRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<AuthRoleLookupItem>> GetCompanyRolesAsync(int companyId)
    {
        return await _context.AuthRoles
            .AsNoTracking()
            .Where(role => role.CompanyId == companyId && role.IsActive)
            .OrderBy(role => role.Code)
            .Select(role => new AuthRoleLookupItem
            {
                Id = role.Id,
                Code = role.Code,
                Name = role.Name
            })
            .ToListAsync();
    }

    public async Task<IReadOnlyDictionary<int, string>> GetRoleCodeMapAsync(int companyId, IEnumerable<int> roleIds)
    {
        var normalizedIds = roleIds
            .Distinct()
            .ToArray();
        if (normalizedIds.Length == 0)
        {
            return new Dictionary<int, string>();
        }

        return await _context.AuthRoles
            .AsNoTracking()
            .Where(role => role.CompanyId == companyId && normalizedIds.Contains(role.Id))
            .ToDictionaryAsync(role => role.Id, role => role.Code);
    }
}
