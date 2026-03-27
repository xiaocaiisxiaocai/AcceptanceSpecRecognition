using System.Security.Claims;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 系统用户管理控制器
/// </summary>
[Route("api/system-users")]
[Authorize]
public class SystemUsersController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthPasswordService _authPasswordService;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<SystemUsersController> _logger;

    public SystemUsersController(
        IUnitOfWork unitOfWork,
        IAuthPasswordService authPasswordService,
        AppDbContext dbContext,
        ILogger<SystemUsersController> logger)
    {
        _unitOfWork = unitOfWork;
        _authPasswordService = authPasswordService;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// 获取系统用户列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<SystemUserDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<SystemUserDto>>>> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] bool? isActive = null)
    {
        var companyId = await ResolveCurrentCompanyIdAsync();
        if (!companyId.HasValue)
            return Error<PagedData<SystemUserDto>>(401, "当前会话缺少公司上下文");

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var (items, total) = await _unitOfWork.SystemUsers.GetPagedAsync(
            page,
            pageSize,
            companyId.Value,
            keyword,
            isActive);

        return Success(new PagedData<SystemUserDto>
        {
            Items = items.Select(ToDto).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取系统用户详情
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<SystemUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SystemUserDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SystemUserDto>>> GetById(int id)
    {
        var companyId = await ResolveCurrentCompanyIdAsync();
        if (!companyId.HasValue)
            return Error<SystemUserDto>(401, "当前会话缺少公司上下文");

        var user = await LoadUserWithAccessAsync(id);
        if (user == null || user.CompanyId != companyId.Value)
            return NotFoundResult<SystemUserDto>("用户不存在");

        return Success(ToDto(user));
    }

    /// <summary>
    /// 创建系统用户
    /// </summary>
    [HttpPost]
    [AuditOperation("create", "system-user")]
    [ProducesResponseType(typeof(ApiResponse<SystemUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SystemUserDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<SystemUserDto>>> Create([FromBody] CreateSystemUserRequest request)
    {
        var companyId = await ResolveCurrentCompanyIdAsync();
        if (!companyId.HasValue)
            return Error<SystemUserDto>(401, "当前会话缺少公司上下文");

        var normalizedUsername = NormalizeUsername(request.Username);
        if (string.IsNullOrWhiteSpace(normalizedUsername))
            return Error<SystemUserDto>(400, "用户名不能为空");

        if (!IsValidUsername(normalizedUsername))
            return Error<SystemUserDto>(400, "用户名仅支持字母、数字、点、下划线、中划线，且长度为3-64");

        if (string.IsNullOrWhiteSpace(request.Password))
            return Error<SystemUserDto>(400, "密码不能为空");

        if (await _unitOfWork.SystemUsers.AnyAsync(u => u.Username == normalizedUsername))
            return Error<SystemUserDto>(400, "用户名已存在");

        var roleCode = NormalizeCode(request.RoleCode);
        if (string.IsNullOrWhiteSpace(roleCode))
            return Error<SystemUserDto>(400, "角色不能为空");

        var role = await _dbContext.AuthRoles
            .FirstOrDefaultAsync(r => r.CompanyId == companyId.Value && r.IsActive && r.Code == roleCode);

        if (role == null)
            return Error<SystemUserDto>(400, "存在无效角色编码");

        var assignedOrgUnitId = await ResolveOrgUnitIdAsync(companyId.Value, request.OrgUnitId);
        if (!assignedOrgUnitId.HasValue)
            return Error<SystemUserDto>(400, "组织节点无效，单组织系统只允许根组织");

        var now = DateTime.UtcNow;
        var user = new SystemUser
        {
            CompanyId = companyId.Value,
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
        var created = await LoadUserWithAccessAsync(user.Id);
        return Success(ToDto(created!), "创建用户成功");
    }

    /// <summary>
    /// 更新系统用户信息
    /// </summary>
    [HttpPut("{id:int}")]
    [AuditOperation("update", "system-user")]
    [ProducesResponseType(typeof(ApiResponse<SystemUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SystemUserDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<SystemUserDto>>> Update(int id, [FromBody] UpdateSystemUserRequest request)
    {
        var companyId = await ResolveCurrentCompanyIdAsync();
        if (!companyId.HasValue)
            return Error<SystemUserDto>(401, "当前会话缺少公司上下文");

        var user = await LoadUserWithAccessAsync(id);
        if (user == null || user.CompanyId != companyId.Value)
            return Error<SystemUserDto>(400, "用户不存在");

        var roleCode = NormalizeCode(request.RoleCode);
        if (string.IsNullOrWhiteSpace(roleCode))
            return Error<SystemUserDto>(400, "角色不能为空");

        var role = await _dbContext.AuthRoles
            .FirstOrDefaultAsync(r => r.CompanyId == companyId.Value && r.IsActive && r.Code == roleCode);
        if (role == null)
            return Error<SystemUserDto>(400, "存在无效角色编码");

        if (!await ValidateAdminBoundaryAsync(
                companyId: companyId.Value,
                targetUser: user,
                nextIsActive: request.IsActive,
                nextRoleCode: roleCode,
                operationName: "更新用户"))
        {
            return Error<SystemUserDto>(400, "系统至少需要保留一个启用状态的 admin 用户");
        }

        var currentUsername = GetCurrentUsername();
        if (!request.IsActive &&
            string.Equals(user.Username, currentUsername, StringComparison.OrdinalIgnoreCase))
        {
            return Error<SystemUserDto>(400, "不能停用当前登录账号");
        }

        var assignedOrgUnitId = await ResolveOrgUnitIdAsync(companyId.Value, request.OrgUnitId);
        if (!assignedOrgUnitId.HasValue)
            return Error<SystemUserDto>(400, "组织节点无效，单组织系统只允许根组织");

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
        });

        await _dbContext.AuthUserOrgUnits.AddAsync(new AuthUserOrgUnit
        {
            UserId = user.Id,
            OrgUnitId = assignedOrgUnitId.Value,
            IsPrimary = true,
            StartAt = request.OrgStartAt,
            EndAt = request.OrgEndAt,
            CreatedAt = DateTime.UtcNow
        });

        _unitOfWork.SystemUsers.Update(user);
        await _unitOfWork.SaveChangesAsync();

        var updated = await LoadUserWithAccessAsync(user.Id);
        return Success(ToDto(updated!), "更新用户成功");
    }

    /// <summary>
    /// 更新用户启用状态
    /// </summary>
    [HttpPut("{id:int}/status")]
    [AuditOperation("update-status", "system-user")]
    [ProducesResponseType(typeof(ApiResponse<SystemUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SystemUserDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<SystemUserDto>>> UpdateStatus(int id, [FromBody] UpdateSystemUserStatusRequest request)
    {
        var companyId = await ResolveCurrentCompanyIdAsync();
        if (!companyId.HasValue)
            return Error<SystemUserDto>(401, "当前会话缺少公司上下文");

        var user = await LoadUserWithAccessAsync(id);
        if (user == null || user.CompanyId != companyId.Value)
            return Error<SystemUserDto>(400, "用户不存在");

        if (!await ValidateAdminBoundaryAsync(
                companyId: companyId.Value,
                targetUser: user,
                nextIsActive: request.IsActive,
                nextRoleCode: GetEffectiveRoleCode(user),
                operationName: "更新状态"))
        {
            return Error<SystemUserDto>(400, "系统至少需要保留一个启用状态的 admin 用户");
        }

        var currentUsername = GetCurrentUsername();
        if (!request.IsActive &&
            string.Equals(user.Username, currentUsername, StringComparison.OrdinalIgnoreCase))
        {
            return Error<SystemUserDto>(400, "不能停用当前登录账号");
        }

        user.IsActive = request.IsActive;
        user.PermissionVersion += 1;
        user.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.SystemUsers.Update(user);
        await _unitOfWork.SaveChangesAsync();

        var updated = await LoadUserWithAccessAsync(user.Id);
        return Success(ToDto(updated!), "更新状态成功");
    }

    /// <summary>
    /// 重置用户密码
    /// </summary>
    [HttpPut("{id:int}/password")]
    [AuditOperation("reset-password", "system-user")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse>> ResetPassword(int id, [FromBody] ResetSystemUserPasswordRequest request)
    {
        var companyId = await ResolveCurrentCompanyIdAsync();
        if (!companyId.HasValue)
            return Error(401, "当前会话缺少公司上下文");

        var user = await _unitOfWork.SystemUsers.GetByIdAsync(id);
        if (user == null || user.CompanyId != companyId.Value)
            return Error(400, "用户不存在");

        if (string.IsNullOrWhiteSpace(request.NewPassword))
            return Error(400, "新密码不能为空");

        user.PasswordHash = _authPasswordService.HashPassword(request.NewPassword);
        user.PermissionVersion += 1;
        user.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.SystemUsers.Update(user);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("重置用户密码成功: {Username}", user.Username);
        return Success("重置密码成功");
    }

    /// <summary>
    /// 删除用户
    /// </summary>
    [HttpDelete("{id:int}")]
    [AuditOperation("delete", "system-user")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        var companyId = await ResolveCurrentCompanyIdAsync();
        if (!companyId.HasValue)
            return Error(401, "当前会话缺少公司上下文");

        var user = await LoadUserWithAccessAsync(id);
        if (user == null || user.CompanyId != companyId.Value)
            return Error(400, "用户不存在");

        if (!await ValidateAdminBoundaryAsync(
                companyId: companyId.Value,
                targetUser: user,
                nextIsActive: false,
                nextRoleCode: null,
                operationName: "删除用户"))
        {
            return Error(400, "系统至少需要保留一个启用状态的 admin 用户");
        }

        var currentUsername = GetCurrentUsername();
        if (string.Equals(user.Username, currentUsername, StringComparison.OrdinalIgnoreCase))
            return Error(400, "不能删除当前登录账号");

        _unitOfWork.SystemUsers.Remove(user);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("删除系统用户成功: {Username}", user.Username);
        return Success("删除用户成功");
    }

    private async Task<SystemUser?> LoadUserWithAccessAsync(int id)
    {
        return await _dbContext.SystemUsers
            .AsSplitQuery()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .Include(u => u.UserOrgUnits)
                .ThenInclude(uo => uo.OrgUnit)
            .FirstOrDefaultAsync(u => u.Id == id);
    }

    private async Task<int?> ResolveCurrentCompanyIdAsync()
    {
        var claim = User.FindFirstValue("company_id");
        if (int.TryParse(claim, out var companyId))
            return companyId;

        // 测试环境兜底：若缺少 company_id 声明，取最小公司ID。
        return await _dbContext.OrgCompanies
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync();
    }

    private async Task<int?> ResolveOrgUnitIdAsync(int companyId, int? orgUnitId)
    {
        if (!orgUnitId.HasValue)
            return null;

        var rootOrgUnitId = await SingleOrgUnitService.GetRootOrgUnitIdAsync(_dbContext, companyId);
        if (!rootOrgUnitId.HasValue)
            return null;

        return rootOrgUnitId.Value == orgUnitId.Value ? rootOrgUnitId.Value : null;
    }

    private async Task<bool> ValidateAdminBoundaryAsync(
        int companyId,
        SystemUser targetUser,
        bool nextIsActive,
        string? nextRoleCode,
        string operationName)
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

    private string GetCurrentUsername()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue(ClaimTypes.Name)
               ?? string.Empty;
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
