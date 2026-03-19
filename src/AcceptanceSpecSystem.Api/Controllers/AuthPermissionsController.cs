using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 权限字典管理
/// </summary>
[Route("api/auth-permissions")]
[Authorize]
public class AuthPermissionsController : BaseApiController
{
    private readonly AppDbContext _dbContext;

    public AuthPermissionsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
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
        var query = _dbContext.AuthPermissions
            .AsNoTracking()
            .Where(p => p.IsActive)
            .AsQueryable();

        if (permissionType.HasValue)
        {
            query = query.Where(p => p.PermissionType == permissionType.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(p =>
                p.Code.Contains(key) ||
                p.Name.Contains(key) ||
                p.Resource.Contains(key) ||
                p.Action.Contains(key));
        }

        var items = await query
            .OrderBy(p => p.PermissionType)
            .ThenBy(p => p.Code)
            .Select(p => new AuthPermissionListItemDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                PermissionType = p.PermissionType,
                Resource = p.Resource,
                Action = p.Action
            })
            .ToListAsync();

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
