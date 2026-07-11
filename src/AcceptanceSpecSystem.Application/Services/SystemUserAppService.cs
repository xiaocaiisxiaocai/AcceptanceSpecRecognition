using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

public interface ISystemUserAppService
{
    Task<PagedData<SystemUserDto>> GetListAsync(
        int companyId,
        int page,
        int pageSize,
        string? keyword,
        bool? isActive,
        CancellationToken cancellationToken = default);

    Task<SystemUserDto?> GetByIdAsync(int companyId, int id, CancellationToken cancellationToken = default);

    Task<SystemUserDto> CreateAsync(
        int companyId,
        CreateSystemUserRequest request,
        CancellationToken cancellationToken = default);

    Task<SystemUserDto> UpdateAsync(
        int companyId,
        int id,
        UpdateSystemUserRequest request,
        string currentUsername,
        CancellationToken cancellationToken = default);

    Task<SystemUserDto> UpdateStatusAsync(
        int companyId,
        int id,
        UpdateSystemUserStatusRequest request,
        string currentUsername,
        CancellationToken cancellationToken = default);

    Task ResetPasswordAsync(
        int companyId,
        int id,
        ResetSystemUserPasswordRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        int companyId,
        int id,
        string currentUsername,
        CancellationToken cancellationToken = default);

    Task<int?> ResolveCurrentCompanyIdAsync(int? claimedCompanyId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 系统用户管理应用服务。
/// </summary>
public sealed class SystemUserAppService : ISystemUserAppService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthPasswordService _authPasswordService;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<SystemUserAppService> _logger;

    public SystemUserAppService(
        IUnitOfWork unitOfWork,
        IAuthPasswordService authPasswordService,
        AppDbContext dbContext,
        ILogger<SystemUserAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _authPasswordService = authPasswordService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PagedData<SystemUserDto>> GetListAsync(
        int companyId,
        int page,
        int pageSize,
        string? keyword,
        bool? isActive,
        CancellationToken cancellationToken = default)
    {
        var (items, total) = await _unitOfWork.SystemUsers.GetPagedAsync(
            page,
            pageSize,
            companyId,
            keyword,
            isActive);

        return new PagedData<SystemUserDto>
        {
            Items = items.Select(ToDto).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<SystemUserDto?> GetByIdAsync(
        int companyId,
        int id,
        CancellationToken cancellationToken = default)
    {
        var user = await LoadUserWithAccessAsync(id, cancellationToken);
        if (user == null || user.CompanyId != companyId)
            return null;

        return ToDto(user);
    }

    public async Task<SystemUserDto> CreateAsync(
        int companyId,
        CreateSystemUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedUsername = NormalizeUsername(request.Username);
        if (string.IsNullOrWhiteSpace(normalizedUsername))
            throw new ApplicationServiceException(400, "用户名不能为空");

        if (!IsValidUsername(normalizedUsername))
            throw new ApplicationServiceException(400, "用户名仅支持字母、数字、点、下划线、中划线，且长度为3-64");

        if (string.IsNullOrWhiteSpace(request.Password))
            throw new ApplicationServiceException(400, "密码不能为空");

        if (await _unitOfWork.SystemUsers.AnyAsync(u => u.Username == normalizedUsername))
            throw new ApplicationServiceException(400, "用户名已存在");

        var roleCode = NormalizeCode(request.RoleCode);
        if (string.IsNullOrWhiteSpace(roleCode))
            throw new ApplicationServiceException(400, "角色不能为空");

        var role = await _dbContext.AuthRoles
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.IsActive && r.Code == roleCode, cancellationToken);
        if (role == null)
            throw new ApplicationServiceException(400, "存在无效角色编码");

        var assignedOrgUnitId = await ResolveOrgUnitIdAsync(companyId, request.OrgUnitId, cancellationToken);
        if (!assignedOrgUnitId.HasValue)
            throw new ApplicationServiceException(400, "组织节点无效，单组织系统只允许根组织");

        var now = DateTime.UtcNow;
        var user = new SystemUser
        {
            CompanyId = companyId,
            Username = normalizedUsername,
            PasswordHash = _authPasswordService.HashPassword(request.Password),
            Nickname = NormalizeNickname(request.Nickname, normalizedUsername),
            Avatar = NormalizeOptional(request.Avatar),
            IsActive = request.IsActive,
            PermissionVersion = 1,
            CreatedAt = now
        };

        user.UserRoles.Add(new AuthUserRole
        {
            RoleId = role.Id,
            StartAt = request.RoleStartAt,
            EndAt = request.RoleEndAt,
            CreatedAt = now
        });

        user.UserOrgUnits.Add(new AuthUserOrgUnit
        {
            OrgUnitId = assignedOrgUnitId.Value,
            IsPrimary = true,
            StartAt = request.OrgStartAt,
            EndAt = request.OrgEndAt,
            CreatedAt = now
        });

        await _unitOfWork.SystemUsers.AddAsync(user);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("创建系统用户成功: {Username}", user.Username);
        return (await GetByIdAsync(companyId, user.Id, cancellationToken))!;
    }

    public async Task<SystemUserDto> UpdateAsync(
        int companyId,
        int id,
        UpdateSystemUserRequest request,
        string currentUsername,
        CancellationToken cancellationToken = default)
    {
        var user = await LoadUserWithAccessAsync(id, cancellationToken);
        if (user == null || user.CompanyId != companyId)
            throw new ApplicationServiceException(400, "用户不存在");

        var roleCode = NormalizeCode(request.RoleCode);
        if (string.IsNullOrWhiteSpace(roleCode))
            throw new ApplicationServiceException(400, "角色不能为空");

        var role = await _dbContext.AuthRoles
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.IsActive && r.Code == roleCode, cancellationToken);
        if (role == null)
            throw new ApplicationServiceException(400, "存在无效角色编码");

        if (!await ValidateAdminBoundaryAsync(companyId, user, request.IsActive, roleCode, "更新用户", cancellationToken))
            throw new ApplicationServiceException(400, "系统至少需要保留一个启用状态的 admin 用户");

        if (!request.IsActive &&
            string.Equals(user.Username, currentUsername, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationServiceException(400, "不能停用当前登录账号");
        }

        var assignedOrgUnitId = await ResolveOrgUnitIdAsync(companyId, request.OrgUnitId, cancellationToken);
        if (!assignedOrgUnitId.HasValue)
            throw new ApplicationServiceException(400, "组织节点无效，单组织系统只允许根组织");

        user.Nickname = NormalizeNickname(request.Nickname, user.Username);
        user.Avatar = NormalizeOptional(request.Avatar);
        user.IsActive = request.IsActive;
        user.PermissionVersion += 1;
        user.UpdatedAt = DateTime.UtcNow;

        _dbContext.AuthUserRoles.RemoveRange(user.UserRoles);
        _dbContext.AuthUserOrgUnits.RemoveRange(user.UserOrgUnits);

        await _dbContext.AuthUserRoles.AddAsync(new AuthUserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            StartAt = request.RoleStartAt,
            EndAt = request.RoleEndAt,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        await _dbContext.AuthUserOrgUnits.AddAsync(new AuthUserOrgUnit
        {
            UserId = user.Id,
            OrgUnitId = assignedOrgUnitId.Value,
            IsPrimary = true,
            StartAt = request.OrgStartAt,
            EndAt = request.OrgEndAt,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        _unitOfWork.SystemUsers.Update(user);
        await _unitOfWork.SaveChangesAsync();

        return (await GetByIdAsync(companyId, user.Id, cancellationToken))!;
    }

    public async Task<SystemUserDto> UpdateStatusAsync(
        int companyId,
        int id,
        UpdateSystemUserStatusRequest request,
        string currentUsername,
        CancellationToken cancellationToken = default)
    {
        var user = await LoadUserWithAccessAsync(id, cancellationToken);
        if (user == null || user.CompanyId != companyId)
            throw new ApplicationServiceException(400, "用户不存在");

        if (!await ValidateAdminBoundaryAsync(
                companyId,
                user,
                request.IsActive,
                GetEffectiveRoleCode(user),
                "更新状态",
                cancellationToken))
            throw new ApplicationServiceException(400, "系统至少需要保留一个启用状态的 admin 用户");

        if (!request.IsActive &&
            string.Equals(user.Username, currentUsername, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationServiceException(400, "不能停用当前登录账号");
        }

        user.IsActive = request.IsActive;
        user.PermissionVersion += 1;
        user.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.SystemUsers.Update(user);
        await _unitOfWork.SaveChangesAsync();

        return (await GetByIdAsync(companyId, user.Id, cancellationToken))!;
    }

    public async Task ResetPasswordAsync(
        int companyId,
        int id,
        ResetSystemUserPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _unitOfWork.SystemUsers.GetByIdAsync(id);
        if (user == null || user.CompanyId != companyId)
            throw new ApplicationServiceException(400, "用户不存在");

        if (string.IsNullOrWhiteSpace(request.NewPassword))
            throw new ApplicationServiceException(400, "新密码不能为空");

        user.PasswordHash = _authPasswordService.HashPassword(request.NewPassword);
        user.PermissionVersion += 1;
        user.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.SystemUsers.Update(user);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("重置用户密码成功: {Username}", user.Username);
    }

    public async Task DeleteAsync(
        int companyId,
        int id,
        string currentUsername,
        CancellationToken cancellationToken = default)
    {
        var user = await LoadUserWithAccessAsync(id, cancellationToken);
        if (user == null || user.CompanyId != companyId)
            throw new ApplicationServiceException(400, "用户不存在");

        if (!await ValidateAdminBoundaryAsync(companyId, user, false, null, "删除用户", cancellationToken))
            throw new ApplicationServiceException(400, "系统至少需要保留一个启用状态的 admin 用户");

        if (string.Equals(user.Username, currentUsername, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationServiceException(400, "不能删除当前登录账号");

        _unitOfWork.SystemUsers.Remove(user);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("删除系统用户成功: {Username}", user.Username);
    }

    public async Task<int?> ResolveCurrentCompanyIdAsync(
        int? claimedCompanyId,
        CancellationToken cancellationToken = default)
    {
        if (claimedCompanyId.HasValue)
            return claimedCompanyId.Value;

        return await _dbContext.OrgCompanies
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<SystemUser?> LoadUserWithAccessAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.SystemUsers
            .AsSplitQuery()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .Include(u => u.UserOrgUnits)
                .ThenInclude(uo => uo.OrgUnit)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    private async Task<int?> ResolveOrgUnitIdAsync(
        int companyId,
        int? orgUnitId,
        CancellationToken cancellationToken = default)
    {
        if (!orgUnitId.HasValue)
            return null;

        var rootOrgUnitId = await _dbContext.OrgUnits
            .AsNoTracking()
            .Where(org => org.CompanyId == companyId && org.ParentId == null && org.UnitType == OrgUnitType.Company)
            .OrderBy(org => org.Id)
            .Select(org => (int?)org.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (!rootOrgUnitId.HasValue)
            return null;

        return rootOrgUnitId.Value == orgUnitId.Value ? rootOrgUnitId.Value : null;
    }

    private async Task<bool> ValidateAdminBoundaryAsync(
        int companyId,
        SystemUser targetUser,
        bool nextIsActive,
        string? nextRoleCode,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        var currentIsActiveAdmin = targetUser.IsActive && HasAdminRole(GetEffectiveRoleCode(targetUser));
        var nextIsActiveAdmin = nextIsActive && HasAdminRole(nextRoleCode);

        if (!currentIsActiveAdmin || nextIsActiveAdmin)
            return true;

        var activeAdminCount = await _unitOfWork.SystemUsers.CountActiveAdminUsersAsync(companyId);
        if (activeAdminCount <= 1)
        {
            _logger.LogWarning("{Operation}被拒绝：尝试移除最后一个启用的admin用户 {Username}", operationName, targetUser.Username);
            return false;
        }

        return true;
    }

    private static string? GetEffectiveRoleCode(SystemUser user)
    {
        var now = DateTime.UtcNow;
        var activeRoleLinks = user.UserRoles
            .Where(ur =>
                ur.Role.IsActive &&
                (!ur.StartAt.HasValue || ur.StartAt <= now) &&
                (!ur.EndAt.HasValue || ur.EndAt >= now))
            .ToList();

        return AuthUserRoleSingleRolePolicy.SelectRoleToKeep(activeRoleLinks)?.Role?.Code;
    }

    private static bool HasAdminRole(string? roleCode)
    {
        return string.Equals(roleCode, "admin", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeUsername(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static bool IsValidUsername(string username)
    {
        if (username.Length < 3 || username.Length > 64)
            return false;

        foreach (var ch in username)
        {
            var ok = char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-';
            if (!ok)
                return false;
        }

        return true;
    }

    private static string NormalizeNickname(string? nickname, string fallback)
    {
        return string.IsNullOrWhiteSpace(nickname) ? fallback : nickname.Trim();
    }

    private static string NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string NormalizeCode(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static SystemUserDto ToDto(SystemUser user)
    {
        var now = DateTime.UtcNow;
        var activeRoleLinks = user.UserRoles
            .Where(ur =>
                ur.Role.IsActive &&
                (!ur.StartAt.HasValue || ur.StartAt <= now) &&
                (!ur.EndAt.HasValue || ur.EndAt >= now))
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

        var activeOrgLinks = user.UserOrgUnits
            .Where(uo =>
                uo.OrgUnit.IsActive &&
                (!uo.StartAt.HasValue || uo.StartAt <= now) &&
                (!uo.EndAt.HasValue || uo.EndAt >= now))
            .ToList();
        var activeOrgLink = AuthUserOrgUnitSingleOrgPolicy.SelectOrgUnitToKeep(activeOrgLinks);

        return new SystemUserDto
        {
            Id = user.Id,
            CompanyId = user.CompanyId,
            Username = user.Username,
            Nickname = user.Nickname,
            Avatar = user.Avatar,
            RoleCode = activeRole?.Code ?? string.Empty,
            RoleName = activeRole?.Name ?? string.Empty,
            Permissions = permissions,
            IsActive = user.IsActive,
            PermissionVersion = user.PermissionVersion,
            OrgUnitId = activeOrgLink?.OrgUnitId,
            OrgUnitName = activeOrgLink?.OrgUnit?.Name ?? string.Empty,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }
}
