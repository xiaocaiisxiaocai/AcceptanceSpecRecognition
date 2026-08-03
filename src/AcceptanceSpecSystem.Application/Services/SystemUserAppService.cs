using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

public sealed record SystemUserActorContext(
    int UserId,
    int CompanyId,
    string Username,
    string RoleCode)
{
    public bool IsAdmin => string.Equals(RoleCode, "admin", StringComparison.OrdinalIgnoreCase);
}

public interface ISystemUserAppService
{
    Task<PagedData<SystemUserDto>> GetListAsync(
        SystemUserActorContext actor,
        int page,
        int pageSize,
        string? keyword,
        bool? isActive,
        CancellationToken cancellationToken = default);

    Task<SystemUserDto?> GetByIdAsync(SystemUserActorContext actor, int id, CancellationToken cancellationToken = default);

    Task<SystemUserDto> CreateAsync(
        SystemUserActorContext actor,
        CreateSystemUserRequest request,
        CancellationToken cancellationToken = default);

    Task<SystemUserDto> UpdateAsync(
        SystemUserActorContext actor,
        int id,
        UpdateSystemUserRequest request,
        CancellationToken cancellationToken = default);

    Task<SystemUserDto> UpdateStatusAsync(
        SystemUserActorContext actor,
        int id,
        UpdateSystemUserStatusRequest request,
        CancellationToken cancellationToken = default);

    Task ResetPasswordAsync(
        SystemUserActorContext actor,
        int id,
        ResetSystemUserPasswordRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        SystemUserActorContext actor,
        int id,
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
        SystemUserActorContext actor,
        int page,
        int pageSize,
        string? keyword,
        bool? isActive,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var query = _dbContext.SystemUsers
            .AsNoTracking()
            .Where(user => user.CompanyId == actor.CompanyId);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var normalizedKeyword = keyword.Trim();
            query = query.Where(user =>
                user.Username.Contains(normalizedKeyword) ||
                user.Nickname.Contains(normalizedKeyword));
        }
        if (isActive.HasValue)
            query = query.Where(user => user.IsActive == isActive.Value);

        if (!actor.IsAdmin)
        {
            var manageableOrgUnitIds = await GetManageableOrgUnitIdsAsync(actor, cancellationToken);
            query = query.Where(user =>
                !user.UserRoles.Any(link =>
                    link.Role.IsActive &&
                    link.Role.Code == "admin" &&
                    (!link.StartAt.HasValue || link.StartAt <= now) &&
                    (!link.EndAt.HasValue || link.EndAt >= now)) &&
                user.UserOrgUnits.Any(link =>
                    manageableOrgUnitIds.Contains(link.OrgUnitId) &&
                    link.OrgUnit.IsActive &&
                    (!link.StartAt.HasValue || link.StartAt <= now) &&
                    (!link.EndAt.HasValue || link.EndAt >= now)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .AsSplitQuery()
            .Include(user => user.UserRoles)
                .ThenInclude(link => link.Role)
                    .ThenInclude(role => role.RolePermissions)
                        .ThenInclude(link => link.Permission)
            .Include(user => user.UserOrgUnits)
                .ThenInclude(link => link.OrgUnit)
            .OrderByDescending(user => user.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedData<SystemUserDto>
        {
            Items = items.Select(ToDto).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<SystemUserDto?> GetByIdAsync(
        SystemUserActorContext actor,
        int id,
        CancellationToken cancellationToken = default)
    {
        var user = await LoadUserWithAccessAsync(id, cancellationToken);
        if (user == null || user.CompanyId != actor.CompanyId)
            return null;
        if (!await CanManageTargetAsync(actor, user, cancellationToken))
            return null;

        return ToDto(user);
    }

    public async Task<SystemUserDto> CreateAsync(
        SystemUserActorContext actor,
        CreateSystemUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedUsername = NormalizeUsername(request.Username);
        if (string.IsNullOrWhiteSpace(normalizedUsername))
            throw new ApplicationServiceException(400, "用户名不能为空");

        if (!IsValidUsername(normalizedUsername))
            throw new ApplicationServiceException(400, "用户名支持中文、字母、数字、点、下划线和中划线，且长度为2-10");

        ValidateNewPassword(request.Password, "密码");

        if (await _unitOfWork.SystemUsers.AnyAsync(u => u.Username == normalizedUsername, cancellationToken))
            throw new ApplicationServiceException(400, "用户名已存在");

        var roleCode = NormalizeCode(request.RoleCode);
        if (string.IsNullOrWhiteSpace(roleCode))
            throw new ApplicationServiceException(400, "角色不能为空");

        var role = await _dbContext.AuthRoles
            .FirstOrDefaultAsync(r => r.CompanyId == actor.CompanyId && r.IsActive && r.Code == roleCode, cancellationToken);
        if (role == null)
            throw new ApplicationServiceException(400, "存在无效角色编码");
        EnsureRoleAssignable(actor, role.Code);

        ValidateRoleInterval(request.RoleStartAt, request.RoleEndAt);
        ValidateOrgInterval(request.OrgStartAt, request.OrgEndAt);

        var assignedOrgUnitId = await ResolveOrgUnitIdAsync(actor.CompanyId, request.OrgUnitId, cancellationToken);
        if (!assignedOrgUnitId.HasValue)
            throw new ApplicationServiceException(400, "组织节点不存在、已停用或不属于当前公司");
        await EnsureOrgUnitManageableAsync(actor, assignedOrgUnitId.Value, cancellationToken);

        var now = DateTime.UtcNow;
        var user = new SystemUser
        {
            CompanyId = actor.CompanyId,
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
        return (await GetByIdAsync(actor, user.Id, cancellationToken))!;
    }

    public async Task<SystemUserDto> UpdateAsync(
        SystemUserActorContext actor,
        int id,
        UpdateSystemUserRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var adminBoundaryLock = await _unitOfWork.AcquireOperationLockAsync(
            BuildAdminBoundaryLockKey(actor.CompanyId),
            cancellationToken);

        var user = await LoadUserWithAccessAsync(id, cancellationToken);
        if (user == null || user.CompanyId != actor.CompanyId)
            throw new ApplicationServiceException(400, "用户不存在");
        await EnsureCanManageTargetAsync(actor, user, cancellationToken);

        var roleCode = NormalizeCode(request.RoleCode);
        if (string.IsNullOrWhiteSpace(roleCode))
            throw new ApplicationServiceException(400, "角色不能为空");

        var role = await _dbContext.AuthRoles
            .FirstOrDefaultAsync(r => r.CompanyId == actor.CompanyId && r.IsActive && r.Code == roleCode, cancellationToken);
        if (role == null)
            throw new ApplicationServiceException(400, "存在无效角色编码");
        EnsureRoleAssignable(actor, role.Code);

        ValidateRoleInterval(request.RoleStartAt, request.RoleEndAt);
        ValidateOrgInterval(request.OrgStartAt, request.OrgEndAt);

        if (!await ValidateAdminBoundaryAsync(
                actor.CompanyId,
                user,
                request.IsActive,
                roleCode,
                request.RoleStartAt,
                request.RoleEndAt,
                "更新用户",
                cancellationToken))
            throw new ApplicationServiceException(400, "系统至少需要保留一个启用状态的 admin 用户，且管理员覆盖区间必须连续");

        if (!request.IsActive &&
            user.Id == actor.UserId)
        {
            throw new ApplicationServiceException(400, "不能停用当前登录账号");
        }

        var assignedOrgUnitId = await ResolveOrgUnitIdAsync(actor.CompanyId, request.OrgUnitId, cancellationToken);
        if (!assignedOrgUnitId.HasValue)
            throw new ApplicationServiceException(400, "组织节点不存在、已停用或不属于当前公司");
        await EnsureOrgUnitManageableAsync(actor, assignedOrgUnitId.Value, cancellationToken);

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

        return (await GetByIdAsync(actor, user.Id, cancellationToken))!;
    }

    public async Task<SystemUserDto> UpdateStatusAsync(
        SystemUserActorContext actor,
        int id,
        UpdateSystemUserStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var adminBoundaryLock = await _unitOfWork.AcquireOperationLockAsync(
            BuildAdminBoundaryLockKey(actor.CompanyId),
            cancellationToken);

        var user = await LoadUserWithAccessAsync(id, cancellationToken);
        if (user == null || user.CompanyId != actor.CompanyId)
            throw new ApplicationServiceException(400, "用户不存在");
        await EnsureCanManageTargetAsync(actor, user, cancellationToken);

        if (!await ValidateAdminBoundaryAsync(
                actor.CompanyId,
                user,
                request.IsActive,
                GetEffectiveRoleCode(user),
                user.UserRoles.SingleOrDefault()?.StartAt,
                user.UserRoles.SingleOrDefault()?.EndAt,
                "更新状态",
                cancellationToken))
            throw new ApplicationServiceException(400, "系统至少需要保留一个启用状态的 admin 用户，且管理员覆盖区间必须连续");

        if (!request.IsActive &&
            user.Id == actor.UserId)
        {
            throw new ApplicationServiceException(400, "不能停用当前登录账号");
        }

        user.IsActive = request.IsActive;
        user.PermissionVersion += 1;
        user.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.SystemUsers.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _refreshSessions.RevokeUserSessionsAsync(user.Id, "account-status-changed", cancellationToken);

        return (await GetByIdAsync(actor, user.Id, cancellationToken))!;
    }

    public async Task ResetPasswordAsync(
        SystemUserActorContext actor,
        int id,
        ResetSystemUserPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await LoadUserWithAccessAsync(id, cancellationToken);
        if (user == null || user.CompanyId != actor.CompanyId)
            throw new ApplicationServiceException(400, "用户不存在");
        await EnsureCanManageTargetAsync(actor, user, cancellationToken);

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
        SystemUserActorContext actor,
        int id,
        CancellationToken cancellationToken = default)
    {
        await using var adminBoundaryLock = await _unitOfWork.AcquireOperationLockAsync(
            BuildAdminBoundaryLockKey(actor.CompanyId),
            cancellationToken);

        var user = await LoadUserWithAccessAsync(id, cancellationToken);
        if (user == null || user.CompanyId != actor.CompanyId)
            throw new ApplicationServiceException(400, "用户不存在");
        await EnsureCanManageTargetAsync(actor, user, cancellationToken);

        if (!await ValidateAdminBoundaryAsync(actor.CompanyId, user, false, null, null, null, "删除用户", cancellationToken))
            throw new ApplicationServiceException(400, "系统至少需要保留一个启用状态的 admin 用户，且管理员覆盖区间必须连续");

        if (user.Id == actor.UserId)
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

        return await _dbContext.OrgUnits
            .AsNoTracking()
            .Where(org =>
                org.Id == orgUnitId.Value &&
                org.CompanyId == companyId &&
                org.IsActive)
            .Select(org => (int?)org.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<HashSet<int>> GetManageableOrgUnitIdsAsync(
        SystemUserActorContext actor,
        CancellationToken cancellationToken)
    {
        if (actor.IsAdmin)
        {
            var allOrgUnitIds = await _dbContext.OrgUnits
                .AsNoTracking()
                .Where(org => org.CompanyId == actor.CompanyId && org.IsActive)
                .Select(org => org.Id)
                .ToListAsync(cancellationToken);
            return allOrgUnitIds.ToHashSet();
        }

        var now = DateTime.UtcNow;
        var activeLinks = await _dbContext.AuthUserOrgUnits
            .AsNoTracking()
            .Include(link => link.OrgUnit)
            .Where(link =>
                link.UserId == actor.UserId &&
                link.OrgUnit.CompanyId == actor.CompanyId &&
                link.OrgUnit.IsActive &&
                (!link.StartAt.HasValue || link.StartAt <= now) &&
                (!link.EndAt.HasValue || link.EndAt >= now))
            .ToListAsync(cancellationToken);
        var current = AuthUserOrgUnitSingleOrgPolicy.SelectOrgUnitToKeep(activeLinks);
        if (current == null)
            return [];

        var manageableOrgUnitIds = await _dbContext.OrgUnits
            .AsNoTracking()
            .Where(org =>
                org.CompanyId == actor.CompanyId &&
                org.IsActive &&
                org.Path.StartsWith(current.OrgUnit.Path))
            .Select(org => org.Id)
            .ToListAsync(cancellationToken);
        return manageableOrgUnitIds.ToHashSet();
    }

    private async Task EnsureOrgUnitManageableAsync(
        SystemUserActorContext actor,
        int orgUnitId,
        CancellationToken cancellationToken)
    {
        if (actor.IsAdmin)
            return;

        var manageable = await GetManageableOrgUnitIdsAsync(actor, cancellationToken);
        if (!manageable.Contains(orgUnitId))
            throw new ApplicationServiceException(403, "普通用户只能管理所属部门的用户");
    }

    private static void EnsureRoleAssignable(SystemUserActorContext actor, string roleCode)
    {
        if (actor.IsAdmin)
            return;

        if (!string.Equals(roleCode, actor.RoleCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationServiceException(403, "普通用户不能授予管理员或其他角色权限");
    }

    private async Task<bool> CanManageTargetAsync(
        SystemUserActorContext actor,
        SystemUser target,
        CancellationToken cancellationToken)
    {
        if (actor.IsAdmin)
            return true;
        if (HasAdminRole(GetEffectiveRoleCode(target)))
            return false;

        var manageable = await GetManageableOrgUnitIdsAsync(actor, cancellationToken);
        var now = DateTime.UtcNow;
        return target.UserOrgUnits.Any(link =>
            manageable.Contains(link.OrgUnitId) &&
            link.OrgUnit.IsActive &&
            (!link.StartAt.HasValue || link.StartAt <= now) &&
            (!link.EndAt.HasValue || link.EndAt >= now));
    }

    private async Task EnsureCanManageTargetAsync(
        SystemUserActorContext actor,
        SystemUser target,
        CancellationToken cancellationToken)
    {
        if (!await CanManageTargetAsync(actor, target, cancellationToken))
            throw new ApplicationServiceException(403, "普通用户不能管理管理员或其他部门的用户");
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
        if (username.Length < 2 || username.Length > 10)
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
