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
    private const int MinimumNewPasswordLength = 4;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthPasswordService _authPasswordService;
    private readonly AppDbContext _dbContext;
    private readonly IAuthRefreshSessionService _refreshSessions;
    private readonly ILogger<SystemUserAppService> _logger;

    public SystemUserAppService(
        IUnitOfWork unitOfWork,
        IAuthPasswordService authPasswordService,
        AppDbContext dbContext,
        IAuthRefreshSessionService refreshSessions,
        ILogger<SystemUserAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _authPasswordService = authPasswordService;
        _dbContext = dbContext;
        _refreshSessions = refreshSessions;
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
            isActive,
            cancellationToken);

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

        ValidateNewPassword(request.Password, "密码");

        if (await _unitOfWork.SystemUsers.AnyAsync(u => u.Username == normalizedUsername, cancellationToken))
            throw new ApplicationServiceException(400, "用户名已存在");

        var roleCode = NormalizeCode(request.RoleCode);
        if (string.IsNullOrWhiteSpace(roleCode))
            throw new ApplicationServiceException(400, "角色不能为空");

        var role = await _dbContext.AuthRoles
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.IsActive && r.Code == roleCode, cancellationToken);
        if (role == null)
            throw new ApplicationServiceException(400, "存在无效角色编码");

        ValidateRoleInterval(request.RoleStartAt, request.RoleEndAt);
        ValidateOrgInterval(request.OrgStartAt, request.OrgEndAt);

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

        await _unitOfWork.SystemUsers.AddAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

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
        await using var adminBoundaryLock = await _unitOfWork.AcquireOperationLockAsync(
            BuildAdminBoundaryLockKey(companyId),
            cancellationToken);

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

        ValidateRoleInterval(request.RoleStartAt, request.RoleEndAt);
        ValidateOrgInterval(request.OrgStartAt, request.OrgEndAt);

        if (!await ValidateAdminBoundaryAsync(
                companyId,
                user,
                request.IsActive,
                roleCode,
                request.RoleStartAt,
                request.RoleEndAt,
                "更新用户",
                cancellationToken))
            throw new ApplicationServiceException(400, "系统至少需要保留一个启用状态的 admin 用户，且管理员覆盖区间必须连续");

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
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _refreshSessions.RevokeUserSessionsAsync(user.Id, "security-context-changed", cancellationToken);

        return (await GetByIdAsync(companyId, user.Id, cancellationToken))!;
    }

    public async Task<SystemUserDto> UpdateStatusAsync(
        int companyId,
        int id,
        UpdateSystemUserStatusRequest request,
        string currentUsername,
        CancellationToken cancellationToken = default)
    {
        await using var adminBoundaryLock = await _unitOfWork.AcquireOperationLockAsync(
            BuildAdminBoundaryLockKey(companyId),
            cancellationToken);

        var user = await LoadUserWithAccessAsync(id, cancellationToken);
        if (user == null || user.CompanyId != companyId)
            throw new ApplicationServiceException(400, "用户不存在");

        if (!await ValidateAdminBoundaryAsync(
                companyId,
                user,
                request.IsActive,
                GetEffectiveRoleCode(user),
                user.UserRoles.SingleOrDefault()?.StartAt,
                user.UserRoles.SingleOrDefault()?.EndAt,
                "更新状态",
                cancellationToken))
            throw new ApplicationServiceException(400, "系统至少需要保留一个启用状态的 admin 用户，且管理员覆盖区间必须连续");

        if (!request.IsActive &&
            string.Equals(user.Username, currentUsername, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationServiceException(400, "不能停用当前登录账号");
        }

        user.IsActive = request.IsActive;
        user.PermissionVersion += 1;
        user.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.SystemUsers.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _refreshSessions.RevokeUserSessionsAsync(user.Id, "account-status-changed", cancellationToken);

        return (await GetByIdAsync(companyId, user.Id, cancellationToken))!;
    }

    public async Task ResetPasswordAsync(
        int companyId,
        int id,
        ResetSystemUserPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _unitOfWork.SystemUsers.GetByIdAsync(id, cancellationToken);
        if (user == null || user.CompanyId != companyId)
            throw new ApplicationServiceException(400, "用户不存在");

        ValidateNewPassword(request.NewPassword, "新密码");

        user.PasswordHash = _authPasswordService.HashPassword(request.NewPassword);
        user.PermissionVersion += 1;
        user.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.SystemUsers.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _refreshSessions.RevokeUserSessionsAsync(user.Id, "password-reset", cancellationToken);

        _logger.LogInformation("重置用户密码成功: {Username}", user.Username);
    }

    public async Task DeleteAsync(
        int companyId,
        int id,
        string currentUsername,
        CancellationToken cancellationToken = default)
    {
        await using var adminBoundaryLock = await _unitOfWork.AcquireOperationLockAsync(
            BuildAdminBoundaryLockKey(companyId),
            cancellationToken);

        var user = await LoadUserWithAccessAsync(id, cancellationToken);
        if (user == null || user.CompanyId != companyId)
            throw new ApplicationServiceException(400, "用户不存在");

        if (!await ValidateAdminBoundaryAsync(companyId, user, false, null, null, null, "删除用户", cancellationToken))
            throw new ApplicationServiceException(400, "系统至少需要保留一个启用状态的 admin 用户，且管理员覆盖区间必须连续");

        if (string.Equals(user.Username, currentUsername, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationServiceException(400, "不能删除当前登录账号");

        _unitOfWork.SystemUsers.Remove(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

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
        DateTime? nextRoleStartAt,
        DateTime? nextRoleEndAt,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var targetHasCurrentOrFutureAdminCoverage = targetUser.IsActive && targetUser.UserRoles.Any(link =>
            link.Role.IsActive &&
            HasAdminRole(link.Role.Code) &&
            (!link.EndAt.HasValue || link.EndAt.Value >= now));
        var nextIsActiveAdmin = nextIsActive && HasAdminRole(nextRoleCode);

        if (!targetHasCurrentOrFutureAdminCoverage && !nextIsActiveAdmin)
            return true;

        var intervals = await _dbContext.SystemUsers
            .AsNoTracking()
            .Where(user => user.CompanyId == companyId && user.Id != targetUser.Id && user.IsActive)
            .SelectMany(user => user.UserRoles)
            .Where(link => link.Role.IsActive && link.Role.Code == "admin")
            .Select(link => new AdminCoverageInterval(link.StartAt, link.EndAt))
            .ToListAsync(cancellationToken);

        if (nextIsActiveAdmin)
        {
            intervals.Add(new AdminCoverageInterval(nextRoleStartAt, nextRoleEndAt));
        }

        if (!HasContinuousAdminCoverage(intervals, now))
        {
            _logger.LogWarning(
                "{Operation}被拒绝：变更会使公司 {CompanyId} 的 admin 覆盖区间出现空档，目标用户 {Username}",
                operationName,
                companyId,
                targetUser.Username);
            return false;
        }

        return true;
    }

    private static bool HasContinuousAdminCoverage(
        IEnumerable<AdminCoverageInterval> intervals,
        DateTime now)
    {
        var candidates = intervals
            .Select(interval => new
            {
                Start = interval.StartAt ?? DateTime.MinValue,
                End = interval.EndAt ?? DateTime.MaxValue
            })
            .Where(interval => interval.End >= now)
            .OrderBy(interval => interval.Start)
            .ThenByDescending(interval => interval.End)
            .ToList();

        var coveringNow = candidates
            .Where(interval => interval.Start <= now && interval.End >= now)
            .ToList();
        if (coveringNow.Count == 0)
            return false;

        var coverageEnd = coveringNow.Max(interval => interval.End);
        if (coverageEnd == DateTime.MaxValue)
            return true;

        foreach (var interval in candidates.Where(interval => interval.Start > now))
        {
            var nextAllowedStart = coverageEnd == DateTime.MaxValue
                ? DateTime.MaxValue
                : coverageEnd.AddTicks(1);
            if (interval.Start > nextAllowedStart)
                break;

            if (interval.End > coverageEnd)
                coverageEnd = interval.End;
            if (coverageEnd == DateTime.MaxValue)
                return true;
        }

        return false;
    }

    private static void ValidateNewPassword(string? password, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ApplicationServiceException(400, $"{fieldName}不能为空");
        if (password.Length < MinimumNewPasswordLength || password.Length > 200)
            throw new ApplicationServiceException(400, $"{fieldName}长度必须在4到200个字符之间");
    }

    private static void ValidateRoleInterval(DateTime? startAt, DateTime? endAt)
    {
        if (startAt.HasValue && endAt.HasValue && startAt.Value > endAt.Value)
            throw new ApplicationServiceException(400, "角色生效时间不能晚于失效时间");
    }

    private static void ValidateOrgInterval(DateTime? startAt, DateTime? endAt)
    {
        if (startAt.HasValue && endAt.HasValue && startAt.Value > endAt.Value)
            throw new ApplicationServiceException(400, "组织生效时间不能晚于失效时间");
    }

    private static string BuildAdminBoundaryLockKey(int companyId) => $"system-user-admin-boundary:{companyId}";

    private sealed record AdminCoverageInterval(DateTime? StartAt, DateTime? EndAt);

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
