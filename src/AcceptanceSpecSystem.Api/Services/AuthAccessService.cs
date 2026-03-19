using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 鉴权聚合上下文
/// </summary>
public sealed class AuthAccessContext
{
    public int UserId { get; init; }

    public int CompanyId { get; init; }

    public string Username { get; init; } = string.Empty;

    public string Nickname { get; init; } = string.Empty;

    public string Avatar { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public int PermissionVersion { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];

    public IReadOnlyList<string> Permissions { get; init; } = [];

    public IReadOnlyList<int> OrgUnitIds { get; init; } = [];

    public int? PrimaryOrgUnitId { get; init; }
}

/// <summary>
/// 角色概要
/// </summary>
public sealed class AuthRoleSummary
{
    public int Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// 鉴权访问服务
/// </summary>
public interface IAuthAccessService
{
    Task<AuthAccessContext?> GetByUsernameAsync(string username);

    Task<AuthAccessContext?> GetByUserIdAsync(int userId);

    Task<IReadOnlyList<AuthRoleSummary>> GetCompanyRolesAsync(int companyId);

    Task<IReadOnlyDictionary<int, string>> GetRoleCodeMapAsync(int companyId, IEnumerable<int> roleIds);
}

/// <summary>
/// 鉴权访问服务实现
/// </summary>
public sealed class AuthAccessService : IAuthAccessService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly AppDbContext _dbContext;

    public AuthAccessService(IUnitOfWork unitOfWork, AppDbContext dbContext)
    {
        _unitOfWork = unitOfWork;
        _dbContext = dbContext;
    }

    public async Task<AuthAccessContext?> GetByUsernameAsync(string username)
    {
        var user = await _unitOfWork.SystemUsers.GetByUsernameWithAccessAsync(username);
        return user == null ? null : BuildContext(user);
    }

    public async Task<AuthAccessContext?> GetByUserIdAsync(int userId)
    {
        var user = await _dbContext.SystemUsers
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .Include(u => u.UserOrgUnits)
                .ThenInclude(uo => uo.OrgUnit)
            .FirstOrDefaultAsync(u => u.Id == userId);

        return user == null ? null : BuildContext(user);
    }

    public async Task<IReadOnlyList<AuthRoleSummary>> GetCompanyRolesAsync(int companyId)
    {
        return await _dbContext.AuthRoles
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.IsActive)
            .OrderBy(r => r.Code)
            .Select(r => new AuthRoleSummary
            {
                Id = r.Id,
                Code = r.Code,
                Name = r.Name
            })
            .ToListAsync();
    }

    public async Task<IReadOnlyDictionary<int, string>> GetRoleCodeMapAsync(int companyId, IEnumerable<int> roleIds)
    {
        var normalizedIds = roleIds
            .Distinct()
            .ToArray();
        if (normalizedIds.Length == 0)
            return new Dictionary<int, string>();

        return await _dbContext.AuthRoles
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && normalizedIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Code);
    }

    private static AuthAccessContext BuildContext(SystemUser user)
    {
        var now = DateTime.Now;
        var activeRoles = user.UserRoles
            .Where(ur => IsActive(now, ur.StartAt, ur.EndAt) && ur.Role.IsActive)
            .Select(ur => ur.Role)
            .DistinctBy(r => r.Id)
            .ToList();

        var permissions = activeRoles
            .SelectMany(r => r.RolePermissions)
            .Where(rp => rp.Permission.IsActive)
            .Select(rp => rp.Permission.Code)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var orgLinks = user.UserOrgUnits
            .Where(uo => IsActive(now, uo.StartAt, uo.EndAt) && uo.OrgUnit.IsActive)
            .ToList();

        var primaryOrgUnitId = orgLinks
            .Where(x => x.IsPrimary)
            .Select(x => (int?)x.OrgUnitId)
            .FirstOrDefault()
            ?? orgLinks.Select(x => (int?)x.OrgUnitId).FirstOrDefault();

        return new AuthAccessContext
        {
            UserId = user.Id,
            CompanyId = user.CompanyId,
            Username = user.Username,
            Nickname = user.Nickname,
            Avatar = user.Avatar,
            IsActive = user.IsActive,
            PermissionVersion = user.PermissionVersion,
            Roles = activeRoles
                .Select(r => r.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Permissions = permissions,
            OrgUnitIds = orgLinks
                .Select(x => x.OrgUnitId)
                .Distinct()
                .OrderBy(x => x)
                .ToList(),
            PrimaryOrgUnitId = primaryOrgUnitId
        };
    }

    private static bool IsActive(DateTime now, DateTime? startAt, DateTime? endAt)
    {
        if (startAt.HasValue && startAt.Value > now)
            return false;
        if (endAt.HasValue && endAt.Value < now)
            return false;
        return true;
    }
}
