using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Application.Services;

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

    public string RoleCode { get; init; } = string.Empty;

    public IReadOnlyList<string> Permissions { get; init; } = [];

    public int? OrgUnitId { get; init; }
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
    Task<AuthAccessContext?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<AuthAccessContext?> GetByUserIdAsync(int userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuthRoleSummary>> GetCompanyRolesAsync(int companyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, string>> GetRoleCodeMapAsync(
        int companyId,
        IEnumerable<int> roleIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 鉴权访问服务实现
/// </summary>
public sealed class AuthAccessService : IAuthAccessService
{
    private readonly ISystemUserRepository _systemUserRepository;
    private readonly IAuthRoleLookupRepository _authRoleLookupRepository;

    public AuthAccessService(
        ISystemUserRepository systemUserRepository,
        IAuthRoleLookupRepository authRoleLookupRepository)
    {
        _systemUserRepository = systemUserRepository;
        _authRoleLookupRepository = authRoleLookupRepository;
    }

    public async Task<AuthAccessContext?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = await _systemUserRepository.GetByUsernameWithAccessAsync(username, cancellationToken);
        return user == null ? null : BuildContext(user);
    }

    public async Task<AuthAccessContext?> GetByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _systemUserRepository.GetByIdWithAccessAsync(userId, cancellationToken);
        return user == null ? null : BuildContext(user);
    }

    public async Task<IReadOnlyList<AuthRoleSummary>> GetCompanyRolesAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        var roles = await _authRoleLookupRepository.GetCompanyRolesAsync(companyId);
        return roles
            .Select(role => new AuthRoleSummary
            {
                Id = role.Id,
                Code = role.Code,
                Name = role.Name
            })
            .ToList();
    }

    public async Task<IReadOnlyDictionary<int, string>> GetRoleCodeMapAsync(
        int companyId,
        IEnumerable<int> roleIds,
        CancellationToken cancellationToken = default)
    {
        return await _authRoleLookupRepository.GetRoleCodeMapAsync(companyId, roleIds);
    }

    private static AuthAccessContext BuildContext(SystemUser user)
    {
        var now = DateTime.UtcNow;
        // 同时收敛角色和组织的当前有效链接，再按单角色/单组织策略各取一个保留项。
        var activeRoleLinks = user.UserRoles
            .Where(ur => IsActive(now, ur.StartAt, ur.EndAt) && ur.Role.IsActive)
            .ToList();
        var activeRoleLink = AuthUserRoleSingleRolePolicy.SelectRoleToKeep(activeRoleLinks);
        var activeRole = activeRoleLink?.Role;

        var permissions = activeRole?.RolePermissions
            .Where(rp => rp.Permission.IsActive)
            .Select(rp => rp.Permission.Code)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? [];

        var orgLinks = user.UserOrgUnits
            .Where(uo => IsActive(now, uo.StartAt, uo.EndAt) && uo.OrgUnit.IsActive)
            .ToList();

        var activeOrgLink = AuthUserOrgUnitSingleOrgPolicy.SelectOrgUnitToKeep(orgLinks);

        return new AuthAccessContext
        {
            UserId = user.Id,
            CompanyId = user.CompanyId,
            Username = user.Username,
            Nickname = user.Nickname,
            Avatar = user.Avatar,
            IsActive = user.IsActive,
            PermissionVersion = user.PermissionVersion,
            RoleCode = activeRole?.Code ?? string.Empty,
            Permissions = permissions,
            OrgUnitId = activeOrgLink?.OrgUnitId
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
