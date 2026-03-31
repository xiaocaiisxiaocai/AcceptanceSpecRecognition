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
        [FromQuery] string? keyword = null)
    {
        var items = await _authPermissionQueryService.GetListAsync(permissionType, keyword);
        return Success(items);
    }
}

/// <summary>
/// 权限列表项 DTO
/// </summary>
public class AuthPermissionListItemDto
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public PermissionType PermissionType { get; set; }

    public string Resource { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;
}
