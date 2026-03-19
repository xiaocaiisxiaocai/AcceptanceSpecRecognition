using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 系统用户仓储实现
/// </summary>
public class SystemUserRepository : Repository<SystemUser>, ISystemUserRepository
{
    public SystemUserRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<SystemUser?> GetByUsernameAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;

        return await _dbSet.FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<SystemUser?> GetByUsernameWithAccessAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;

        var now = DateTime.Now;
        return await _dbSet
            .Include(u => u.UserRoles.Where(r =>
                (!r.StartAt.HasValue || r.StartAt <= now) &&
                (!r.EndAt.HasValue || r.EndAt >= now)))
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .Include(u => u.UserRoles.Where(r =>
                (!r.StartAt.HasValue || r.StartAt <= now) &&
                (!r.EndAt.HasValue || r.EndAt >= now)))
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.DataScopes)
                        .ThenInclude(s => s.Nodes)
                            .ThenInclude(n => n.OrgUnit)
            .Include(u => u.UserOrgUnits.Where(o =>
                (!o.StartAt.HasValue || o.StartAt <= now) &&
                (!o.EndAt.HasValue || o.EndAt >= now)))
                .ThenInclude(o => o.OrgUnit)
            .FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<(IReadOnlyList<SystemUser> Items, int Total)> GetPagedAsync(
        int page,
        int pageSize,
        int? companyId = null,
        string? keyword = null,
        bool? isActive = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _dbSet.AsNoTracking().AsQueryable();
        if (companyId.HasValue)
        {
            query = query.Where(u => u.CompanyId == companyId.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(u =>
                u.Username.Contains(key) ||
                u.Nickname.Contains(key));
        }

        if (isActive.HasValue)
        {
            query = query.Where(u => u.IsActive == isActive.Value);
        }

        query = query
            .OrderByDescending(u => u.IsActive)
            .ThenBy(u => u.Username);

        var total = await query.CountAsync();
        var items = await query
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .Include(u => u.UserOrgUnits)
                .ThenInclude(uo => uo.OrgUnit)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<int> CountActiveAdminUsersAsync(int companyId)
    {
        var now = DateTime.Now;
        return await _dbSet
            .AsNoTracking()
            .CountAsync(u =>
                u.CompanyId == companyId &&
                u.IsActive &&
                u.UserRoles.Any(ur =>
                    (!ur.StartAt.HasValue || ur.StartAt <= now) &&
                    (!ur.EndAt.HasValue || ur.EndAt >= now) &&
                    ur.Role.IsActive &&
                    ur.Role.Code == "admin"));
    }
}
