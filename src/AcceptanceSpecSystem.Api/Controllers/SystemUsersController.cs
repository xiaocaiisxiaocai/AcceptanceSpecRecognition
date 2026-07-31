using System.Security.Claims;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 系统用户管理控制器
/// </summary>
[Route("api/system-users")]
[Authorize]
public class SystemUsersController : BaseApiController
{
    private readonly ISystemUserAppService _systemUserAppService;

    public SystemUsersController(ISystemUserAppService systemUserAppService)
    {
        _systemUserAppService = systemUserAppService;
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
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _systemUserAppService.ResolveCurrentCompanyIdAsync(
            AuthClaimHelper.GetCompanyId(User), cancellationToken);
        if (!companyId.HasValue)
            return Error<PagedData<SystemUserDto>>(401, "当前会话缺少公司上下文");

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var actor = CreateActor(companyId.Value);
        if (actor == null)
            return Error<PagedData<SystemUserDto>>(401, "当前会话缺少用户上下文");

        var data = await _systemUserAppService.GetListAsync(actor, page, pageSize, keyword, isActive, cancellationToken);
        return Success(data);
    }

    /// <summary>
    /// 获取系统用户详情
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<SystemUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SystemUserDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SystemUserDto>>> GetById(
        int id,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _systemUserAppService.ResolveCurrentCompanyIdAsync(
            AuthClaimHelper.GetCompanyId(User), cancellationToken);
        if (!companyId.HasValue)
            return Error<SystemUserDto>(401, "当前会话缺少公司上下文");

        var actor = CreateActor(companyId.Value);
        if (actor == null)
            return Error<SystemUserDto>(401, "当前会话缺少用户上下文");

        var user = await _systemUserAppService.GetByIdAsync(actor, id, cancellationToken);
        if (user == null)
            return NotFoundResult<SystemUserDto>("用户不存在");

        return Success(user);
    }

    /// <summary>
    /// 创建系统用户
    /// </summary>
    [HttpPost]
    [AuditOperation("create", "system-user")]
    [ProducesResponseType(typeof(ApiResponse<SystemUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SystemUserDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<SystemUserDto>>> Create(
        [FromBody] CreateSystemUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _systemUserAppService.ResolveCurrentCompanyIdAsync(
            AuthClaimHelper.GetCompanyId(User), cancellationToken);
        if (!companyId.HasValue)
            return Error<SystemUserDto>(401, "当前会话缺少公司上下文");

        try
        {
            var actor = CreateActor(companyId.Value);
            if (actor == null)
                return Error<SystemUserDto>(401, "当前会话缺少用户上下文");

            var user = await _systemUserAppService.CreateAsync(actor, request, cancellationToken);
            return Success(user, "创建用户成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<SystemUserDto>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 更新系统用户信息
    /// </summary>
    [HttpPut("{id:int}")]
    [AuditOperation("update", "system-user")]
    [ProducesResponseType(typeof(ApiResponse<SystemUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SystemUserDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<SystemUserDto>>> Update(
        int id,
        [FromBody] UpdateSystemUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _systemUserAppService.ResolveCurrentCompanyIdAsync(
            AuthClaimHelper.GetCompanyId(User), cancellationToken);
        if (!companyId.HasValue)
            return Error<SystemUserDto>(401, "当前会话缺少公司上下文");

        try
        {
            var actor = CreateActor(companyId.Value);
            if (actor == null)
                return Error<SystemUserDto>(401, "当前会话缺少用户上下文");

            var user = await _systemUserAppService.UpdateAsync(actor, id, request, cancellationToken);
            return Success(user, "更新用户成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<SystemUserDto>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 更新用户启用状态
    /// </summary>
    [HttpPut("{id:int}/status")]
    [AuditOperation("update-status", "system-user")]
    [ProducesResponseType(typeof(ApiResponse<SystemUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SystemUserDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<SystemUserDto>>> UpdateStatus(
        int id,
        [FromBody] UpdateSystemUserStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _systemUserAppService.ResolveCurrentCompanyIdAsync(
            AuthClaimHelper.GetCompanyId(User), cancellationToken);
        if (!companyId.HasValue)
            return Error<SystemUserDto>(401, "当前会话缺少公司上下文");

        try
        {
            var actor = CreateActor(companyId.Value);
            if (actor == null)
                return Error<SystemUserDto>(401, "当前会话缺少用户上下文");

            var user = await _systemUserAppService.UpdateStatusAsync(actor, id, request, cancellationToken);
            return Success(user, "更新状态成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<SystemUserDto>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 重置用户密码
    /// </summary>
    [HttpPut("{id:int}/password")]
    [AuditOperation("reset-password", "system-user")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse>> ResetPassword(
        int id,
        [FromBody] ResetSystemUserPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _systemUserAppService.ResolveCurrentCompanyIdAsync(
            AuthClaimHelper.GetCompanyId(User), cancellationToken);
        if (!companyId.HasValue)
            return Error(401, "当前会话缺少公司上下文");

        try
        {
            var actor = CreateActor(companyId.Value);
            if (actor == null)
                return Error(401, "当前会话缺少用户上下文");

            await _systemUserAppService.ResetPasswordAsync(actor, id, request, cancellationToken);
            return Success("重置密码成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 删除用户
    /// </summary>
    [HttpDelete("{id:int}")]
    [AuditOperation("delete", "system-user")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse>> Delete(
        int id,
        CancellationToken cancellationToken = default)
    {
        var companyId = await _systemUserAppService.ResolveCurrentCompanyIdAsync(
            AuthClaimHelper.GetCompanyId(User), cancellationToken);
        if (!companyId.HasValue)
            return Error(401, "当前会话缺少公司上下文");

        try
        {
            var actor = CreateActor(companyId.Value);
            if (actor == null)
                return Error(401, "当前会话缺少用户上下文");

            await _systemUserAppService.DeleteAsync(actor, id, cancellationToken);
            return Success("删除用户成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error(ex.Code, ex.Message);
        }
    }

    private SystemUserActorContext? CreateActor(int companyId)
    {
        var userId = AuthClaimHelper.GetUserId(User);
        if (!userId.HasValue)
            return null;

        var username = User.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? User.FindFirstValue(ClaimTypes.Name)
                       ?? string.Empty;
        var roleCode = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        return new SystemUserActorContext(userId.Value, companyId, username, roleCode);
    }
}
