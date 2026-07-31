using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using System.Security.Claims;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 角色管理控制器
/// </summary>
[Route("api/auth-roles")]
[Authorize]
public class AuthRolesController : BaseApiController
{
    private readonly IAuthRoleAppService _authRoleAppService;

    public AuthRolesController(IAuthRoleAppService authRoleAppService)
    {
        _authRoleAppService = authRoleAppService;
    }

    /// <summary>
    /// 获取角色列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<AuthRoleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<AuthRoleDto>>>> GetList(
        [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error<List<AuthRoleDto>>(401, "会话缺少公司上下文");

        var roles = await _authRoleAppService.GetListAsync(companyId.Value, keyword, cancellationToken);
        if (!User.IsInRole("admin"))
        {
            var currentRoleCode = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            roles = roles
                .Where(role => string.Equals(role.Code, currentRoleCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        return Success(roles);
    }

    /// <summary>
    /// 获取角色详情
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<AuthRoleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AuthRoleDto>>> GetById(
        int id,
        CancellationToken cancellationToken = default)
    {
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error<AuthRoleDto>(401, "会话缺少公司上下文");

        var role = await _authRoleAppService.GetByIdAsync(companyId.Value, id, cancellationToken);
        if (role == null)
            return NotFoundResult<AuthRoleDto>("角色不存在");
        if (!User.IsInRole("admin") &&
            !string.Equals(role.Code, User.FindFirstValue(ClaimTypes.Role), StringComparison.OrdinalIgnoreCase))
        {
            return Error<AuthRoleDto>(403, "普通用户不能查看其他角色权限");
        }

        return Success(role);
    }

    /// <summary>
    /// 创建角色
    /// </summary>
    [HttpPost]
    [AuditOperation("create", "auth-role")]
    [ProducesResponseType(typeof(ApiResponse<AuthRoleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AuthRoleDto>>> Create(
        [FromBody] CreateAuthRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!User.IsInRole("admin"))
            return Error<AuthRoleDto>(403, "只有管理员可以创建角色");

        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error<AuthRoleDto>(401, "会话缺少公司上下文");

        try
        {
            var role = await _authRoleAppService.CreateAsync(companyId.Value, request, cancellationToken);
            return Success(role, "创建角色成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<AuthRoleDto>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 更新角色
    /// </summary>
    [HttpPut("{id:int}")]
    [AuditOperation("update", "auth-role")]
    [ProducesResponseType(typeof(ApiResponse<AuthRoleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AuthRoleDto>>> Update(
        int id,
        [FromBody] UpdateAuthRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!User.IsInRole("admin"))
            return Error<AuthRoleDto>(403, "只有管理员可以修改角色权限");

        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error<AuthRoleDto>(401, "会话缺少公司上下文");

        try
        {
            var role = await _authRoleAppService.UpdateAsync(companyId.Value, id, request, cancellationToken);
            return Success(role, "更新角色成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<AuthRoleDto>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 删除角色
    /// </summary>
    [HttpDelete("{id:int}")]
    [AuditOperation("delete", "auth-role")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Delete(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!User.IsInRole("admin"))
            return Error(403, "只有管理员可以删除角色");

        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error(401, "会话缺少公司上下文");

        try
        {
            await _authRoleAppService.DeleteAsync(companyId.Value, id, cancellationToken);
            return Success("删除角色成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error(ex.Code, ex.Message);
        }
    }
}
