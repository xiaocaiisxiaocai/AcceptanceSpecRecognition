using AcceptanceSpecSystem.Api.Controllers;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 权限字典查询应用服务。
/// </summary>
public sealed class AuthPermissionQueryService
{
    private readonly AppDbContext _dbContext;

    public AuthPermissionQueryService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<AuthPermissionListItemDto>> GetListAsync(
        PermissionType? permissionType = null,
        string? keyword = null)
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

        return await query
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
    }
}
