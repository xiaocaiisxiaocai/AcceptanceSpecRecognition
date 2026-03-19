using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 角色管理控制器
/// </summary>
[Route("api/auth-roles")]
[Authorize]
public class AuthRolesController : BaseApiController
{
    private readonly AppDbContext _dbContext;

    public AuthRolesController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// 获取角色列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<AuthRoleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<AuthRoleDto>>>> GetList([FromQuery] string? keyword = null)
    {
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error<List<AuthRoleDto>>(401, "会话缺少公司上下文");

        var query = _dbContext.AuthRoles
            .AsNoTracking()
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .Include(r => r.DataScopes)
                .ThenInclude(s => s.Nodes)
            .Where(r => r.CompanyId == companyId.Value);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(r => r.Code.Contains(key) || r.Name.Contains(key));
        }

        var roles = await query
            .OrderByDescending(r => r.IsBuiltIn)
            .ThenBy(r => r.Code)
            .ToListAsync();

        return Success(roles.Select(ToDto).ToList());
    }

    /// <summary>
    /// 获取角色详情
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<AuthRoleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AuthRoleDto>>> GetById(int id)
    {
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error<AuthRoleDto>(401, "会话缺少公司上下文");

        var role = await _dbContext.AuthRoles
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .Include(r => r.DataScopes)
                .ThenInclude(s => s.Nodes)
            .FirstOrDefaultAsync(r => r.CompanyId == companyId.Value && r.Id == id);
        if (role == null)
            return Error<AuthRoleDto>(404, "角色不存在");

        return Success(ToDto(role));
    }

    /// <summary>
    /// 创建角色
    /// </summary>
    [HttpPost]
    [AuditOperation("create", "auth-role")]
    [ProducesResponseType(typeof(ApiResponse<AuthRoleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AuthRoleDto>>> Create([FromBody] CreateAuthRoleRequest request)
    {
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error<AuthRoleDto>(401, "会话缺少公司上下文");

        var code = NormalizeCode(request.Code);
        if (string.IsNullOrWhiteSpace(code))
            return Error<AuthRoleDto>(400, "角色编码不能为空");

        if (await _dbContext.AuthRoles.AnyAsync(r => r.CompanyId == companyId.Value && r.Code == code))
            return Error<AuthRoleDto>(400, "角色编码已存在");

        var now = DateTime.Now;
        var role = new AuthRole
        {
            CompanyId = companyId.Value,
            Code = code,
            Name = request.Name.Trim(),
            Description = NormalizeOptional(request.Description),
            IsBuiltIn = false,
            IsActive = request.IsActive,
            CreatedAt = now
        };

        await _dbContext.AuthRoles.AddAsync(role);
        await _dbContext.SaveChangesAsync();

        var syncError = await SyncRoleRelationsAsync(role, request.PermissionCodes, request.DataScopes, companyId.Value);
        if (!string.IsNullOrWhiteSpace(syncError))
            return Error<AuthRoleDto>(400, syncError);

        await _dbContext.SaveChangesAsync();
        await _dbContext.Entry(role).Collection(r => r.RolePermissions).LoadAsync();
        await _dbContext.Entry(role).Collection(r => r.DataScopes).LoadAsync();

        var saved = await _dbContext.AuthRoles
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .Include(r => r.DataScopes).ThenInclude(s => s.Nodes)
            .FirstAsync(r => r.Id == role.Id);

        return Success(ToDto(saved), "创建角色成功");
    }

    /// <summary>
    /// 更新角色
    /// </summary>
    [HttpPut("{id:int}")]
    [AuditOperation("update", "auth-role")]
    [ProducesResponseType(typeof(ApiResponse<AuthRoleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AuthRoleDto>>> Update(int id, [FromBody] UpdateAuthRoleRequest request)
    {
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error<AuthRoleDto>(401, "会话缺少公司上下文");

        var role = await _dbContext.AuthRoles
            .Include(r => r.RolePermissions)
            .Include(r => r.DataScopes)
                .ThenInclude(s => s.Nodes)
            .FirstOrDefaultAsync(r => r.CompanyId == companyId.Value && r.Id == id);
        if (role == null)
            return Error<AuthRoleDto>(404, "角色不存在");

        role.Name = request.Name.Trim();
        role.Description = NormalizeOptional(request.Description);
        if (!role.IsBuiltIn)
        {
            role.IsActive = request.IsActive;
        }
        role.UpdatedAt = DateTime.Now;

        var syncError = await SyncRoleRelationsAsync(role, request.PermissionCodes, request.DataScopes, companyId.Value);
        if (!string.IsNullOrWhiteSpace(syncError))
            return Error<AuthRoleDto>(400, syncError);

        await _dbContext.SaveChangesAsync();

        var saved = await _dbContext.AuthRoles
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .Include(r => r.DataScopes).ThenInclude(s => s.Nodes)
            .FirstAsync(r => r.Id == role.Id);

        return Success(ToDto(saved), "更新角色成功");
    }

    /// <summary>
    /// 删除角色
    /// </summary>
    [HttpDelete("{id:int}")]
    [AuditOperation("delete", "auth-role")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error(401, "会话缺少公司上下文");

        var role = await _dbContext.AuthRoles.FirstOrDefaultAsync(r => r.CompanyId == companyId.Value && r.Id == id);
        if (role == null)
            return Error(404, "角色不存在");

        if (role.IsBuiltIn)
            return Error(400, "内置角色不允许删除");

        var referenced = await _dbContext.AuthUserRoles.AnyAsync(r => r.RoleId == id);
        if (referenced)
            return Error(400, "角色已被用户使用，无法删除");

        _dbContext.AuthRoles.Remove(role);
        await _dbContext.SaveChangesAsync();

        return Success("删除角色成功");
    }

    private async Task<string?> SyncRoleRelationsAsync(
        AuthRole role,
        IEnumerable<string> permissionCodes,
        IEnumerable<AuthRoleDataScopeDto> dataScopes,
        int companyId)
    {
        var normalizedPermissionCodes = permissionCodes
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var permissions = normalizedPermissionCodes.Count == 0
            ? []
            : await _dbContext.AuthPermissions
                .Where(p => normalizedPermissionCodes.Contains(p.Code))
                .ToListAsync();
        if (permissions.Count != normalizedPermissionCodes.Count)
            return "存在无效权限编码";

        var currentPermissionIds = role.RolePermissions.Select(x => x.PermissionId).ToHashSet();
        var targetPermissionIds = permissions.Select(x => x.Id).ToHashSet();
        var removeRolePermissions = role.RolePermissions.Where(x => !targetPermissionIds.Contains(x.PermissionId)).ToList();
        if (removeRolePermissions.Count > 0)
            _dbContext.AuthRolePermissions.RemoveRange(removeRolePermissions);

        foreach (var permissionId in targetPermissionIds.Where(id => !currentPermissionIds.Contains(id)))
        {
            await _dbContext.AuthRolePermissions.AddAsync(new AuthRolePermission
            {
                RoleId = role.Id,
                PermissionId = permissionId
            });
        }

        var normalizedScopes = dataScopes
            .Where(x => !string.IsNullOrWhiteSpace(x.Resource))
            .Select(x => new AuthRoleDataScopeDto
            {
                Resource = x.Resource.Trim().ToLowerInvariant(),
                ScopeType = x.ScopeType,
                OrgUnitIds = x.OrgUnitIds?.Distinct().ToList() ?? []
            })
            .ToList();

        var allNodeIds = normalizedScopes
            .SelectMany(x => x.OrgUnitIds)
            .Distinct()
            .ToList();
        if (allNodeIds.Count > 0)
        {
            var validNodeIds = await _dbContext.OrgUnits
                .Where(o => o.CompanyId == companyId && allNodeIds.Contains(o.Id))
                .Select(o => o.Id)
                .ToListAsync();
            if (validNodeIds.Count != allNodeIds.Count)
                return "数据范围中存在无效组织节点";
        }

        var existingScopes = await _dbContext.AuthRoleDataScopes
            .Include(s => s.Nodes)
            .Where(s => s.RoleId == role.Id)
            .ToListAsync();

        _dbContext.AuthRoleDataScopes.RemoveRange(existingScopes);
        await _dbContext.SaveChangesAsync();

        foreach (var scope in normalizedScopes)
        {
            var scopeEntity = new AuthRoleDataScope
            {
                RoleId = role.Id,
                Resource = scope.Resource,
                ScopeType = scope.ScopeType,
                CreatedAt = DateTime.Now
            };
            await _dbContext.AuthRoleDataScopes.AddAsync(scopeEntity);
            await _dbContext.SaveChangesAsync();

            foreach (var nodeId in scope.OrgUnitIds)
            {
                await _dbContext.AuthRoleDataScopeNodes.AddAsync(new AuthRoleDataScopeNode
                {
                    RoleDataScopeId = scopeEntity.Id,
                    OrgUnitId = nodeId
                });
            }

            await _dbContext.SaveChangesAsync();
        }

        return null;
    }

    private static string NormalizeCode(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
    }

    private static string NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static AuthRoleDto ToDto(AuthRole role)
    {
        return new AuthRoleDto
        {
            Id = role.Id,
            Code = role.Code,
            Name = role.Name,
            Description = role.Description,
            IsBuiltIn = role.IsBuiltIn,
            IsActive = role.IsActive,
            PermissionCodes = role.RolePermissions
                .Select(p => p.Permission.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            DataScopes = role.DataScopes
                .Select(s => new AuthRoleDataScopeDto
                {
                    Resource = s.Resource,
                    ScopeType = s.ScopeType,
                    OrgUnitIds = s.Nodes.Select(n => n.OrgUnitId).Distinct().OrderBy(x => x).ToList()
                })
                .OrderBy(s => s.Resource)
                .ThenBy(s => s.ScopeType)
                .ToList()
        };
    }
}
