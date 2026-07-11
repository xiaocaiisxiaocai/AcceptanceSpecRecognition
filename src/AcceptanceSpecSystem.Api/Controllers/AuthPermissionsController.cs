using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 权限字典管理
/// </summary>
[Route("api/auth-permissions")]
[Authorize]
public class AuthPermissionsController : BaseApiController
{
    private readonly AuthPermissionQueryService _authPermissionQueryService;

    public AuthPermissionsController(AuthPermissionQueryService authPermissionQueryService)
    {
        _authPermissionQueryService = authPermissionQueryService;
    }

    /// <summary>
    /// 获取权限字典列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<AuthPermissionListItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<AuthPermissionListItemDto>>>> GetList(
        [FromQuery] PermissionType? permissionType = null,
        [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        var items = await _authPermissionQueryService.GetListAsync(permissionType, keyword, cancellationToken);
        return Success(items);
    }
}
