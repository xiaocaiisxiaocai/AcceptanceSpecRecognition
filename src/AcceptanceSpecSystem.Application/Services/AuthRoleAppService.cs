using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

public interface IAuthRoleAppService
{
    Task<List<AuthRoleDto>> GetListAsync(
        int companyId,
        string? keyword = null,
        CancellationToken cancellationToken = default);

    Task<AuthRoleDto?> GetByIdAsync(int companyId, int id, CancellationToken cancellationToken = default);

    Task<AuthRoleDto> CreateAsync(
        int companyId,
        CreateAuthRoleRequest request,
        CancellationToken cancellationToken = default);

    Task<AuthRoleDto> UpdateAsync(
        int companyId,
        int id,
        UpdateAuthRoleRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(int companyId, int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 角色管理应用服务。
/// </summary>
public sealed class AuthRoleAppService : IAuthRoleAppService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuthRefreshSessionService _refreshSessions;

    public AuthRoleAppService(AppDbContext dbContext, IAuthRefreshSessionService refreshSessions)
    {
        _dbContext = dbContext;
        _refreshSessions = refreshSessions;
    }

    public async Task<List<AuthRoleDto>> GetListAsync(
        int companyId,
        string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.AuthRoles
            .AsNoTracking()
            .AsSplitQuery()
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .Include(r => r.DataScopes)
                .ThenInclude(s => s.Nodes)
            .Where(r => r.CompanyId == companyId);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(r => r.Code.Contains(key) || r.Name.Contains(key));
        }

        var roles = await query
            .OrderByDescending(r => r.IsBuiltIn)
            .ThenBy(r => r.Code)
            .ToListAsync(cancellationToken);

        return roles.Select(ToDto).ToList();
    }

    public async Task<AuthRoleDto?> GetByIdAsync(
        int companyId,
        int id,
        CancellationToken cancellationToken = default)
    {
        var role = await _dbContext.AuthRoles
            .AsSplitQuery()
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .Include(r => r.DataScopes)
                .ThenInclude(s => s.Nodes)
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.Id == id, cancellationToken);

        return role == null ? null : ToDto(role);
    }

    public async Task<AuthRoleDto> CreateAsync(
        int companyId,
        CreateAuthRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = NormalizeCode(request.Code);
        if (string.IsNullOrWhiteSpace(code))
            throw new ApplicationServiceException(400, "角色编码不能为空");

        if (await _dbContext.AuthRoles.AnyAsync(r => r.CompanyId == companyId && r.Code == code, cancellationToken))
            throw new ApplicationServiceException(400, "角色编码已存在");

        var now = DateTime.UtcNow;
        var role = new AuthRole
        {
            CompanyId = companyId,
            Code = code,
            Name = request.Name.Trim(),
            Description = NormalizeOptional(request.Description),
            IsBuiltIn = false,
            IsActive = request.IsActive,
            CreatedAt = now
        };

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _dbContext.AuthRoles.AddAsync(role, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var syncError = await SyncRoleRelationsAsync(
            role,
            request.PermissionCodes,
            request.DataScopes,
            companyId,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(syncError))
            throw new ApplicationServiceException(400, syncError);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await GetByIdAsync(companyId, role.Id, cancellationToken))!;
    }

    public async Task<AuthRoleDto> UpdateAsync(
        int companyId,
        int id,
        UpdateAuthRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var role = await _dbContext.AuthRoles
            .AsSplitQuery()
            .Include(r => r.RolePermissions)
            .Include(r => r.DataScopes)
                .ThenInclude(s => s.Nodes)
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.Id == id, cancellationToken);
        if (role == null)
            throw new ApplicationServiceException(404, "角色不存在");

        if (role.IsBuiltIn)
            throw new ApplicationServiceException(400, "内置角色不允许修改");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        role.Name = request.Name.Trim();
        role.Description = NormalizeOptional(request.Description);
        role.IsActive = request.IsActive;
        role.UpdatedAt = DateTime.UtcNow;

        var syncError = await SyncRoleRelationsAsync(
            role,
            request.PermissionCodes,
            request.DataScopes,
            companyId,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(syncError))
            throw new ApplicationServiceException(400, syncError);

        var affectedUserIds = await _dbContext.SystemUsers
            .Where(user => user.UserRoles.Any(userRole => userRole.RoleId == role.Id))
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
        await TouchUsersByRoleAsync(role.Id, cancellationToken);
        await _refreshSessions.RevokeUserSessionsAsync(
            affectedUserIds,
            "role-security-context-changed",
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await GetByIdAsync(companyId, role.Id, cancellationToken))!;
    }

    public async Task DeleteAsync(int companyId, int id, CancellationToken cancellationToken = default)
    {
        var role = await _dbContext.AuthRoles.FirstOrDefaultAsync(
            r => r.CompanyId == companyId && r.Id == id,
            cancellationToken);
        if (role == null)
            throw new ApplicationServiceException(404, "角色不存在");

        if (role.IsBuiltIn)
            throw new ApplicationServiceException(400, "内置角色不允许删除");

        var referenced = await _dbContext.AuthUserRoles.AnyAsync(r => r.RoleId == id, cancellationToken);
        if (referenced)
            throw new ApplicationServiceException(400, "角色已被用户使用，无法删除");

        _dbContext.AuthRoles.Remove(role);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<string?> SyncRoleRelationsAsync(
        AuthRole role,
        IEnumerable<string> permissionCodes,
        IEnumerable<AuthRoleDataScopeDto> dataScopes,
        int companyId,
        CancellationToken cancellationToken = default)
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
                .ToListAsync(cancellationToken);
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
            }, cancellationToken);
        }

        var normalizedScopes = dataScopes
            .Where(x => !string.IsNullOrWhiteSpace(x.Resource))
            .Select(x => new AuthRoleDataScopeDto
            {
                Resource = x.Resource.Trim().ToLowerInvariant(),
                ScopeType = x.ScopeType,
                OrgUnitIds = x.ScopeType is DataScopeType.Self or DataScopeType.All
                    ? new List<int>()
                    : x.OrgUnitIds?.Distinct().ToList() ?? new List<int>()
            })
            .ToList();

        if (normalizedScopes.Any(scope => !Enum.IsDefined(scope.ScopeType)))
            return "存在无效的数据范围类型";
        if (normalizedScopes.Any(scope =>
                scope.ScopeType is DataScopeType.OrgNode or DataScopeType.OrgSubtree &&
                scope.OrgUnitIds.Count != 1))
            return "单个组织或组织及子树范围必须选择一个组织节点";
        if (normalizedScopes.Any(scope =>
                scope.ScopeType == DataScopeType.CustomNodes &&
                scope.OrgUnitIds.Count == 0))
            return "自定义组织范围至少需要选择一个组织节点";

        var allNodeIds = normalizedScopes
            .SelectMany(x => x.OrgUnitIds)
            .Distinct()
            .ToList();
        if (allNodeIds.Count > 0)
        {
            var validNodeCount = await _dbContext.OrgUnits
                .AsNoTracking()
                .CountAsync(
                    org => allNodeIds.Contains(org.Id) &&
                           org.CompanyId == companyId &&
                           org.IsActive,
                    cancellationToken);
            if (validNodeCount != allNodeIds.Count)
                return "数据范围包含不存在、已停用或不属于当前公司的组织节点";
        }

        var existingScopes = await _dbContext.AuthRoleDataScopes
            .Include(s => s.Nodes)
            .Where(s => s.RoleId == role.Id)
            .ToListAsync(cancellationToken);

        _dbContext.AuthRoleDataScopes.RemoveRange(existingScopes);
        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var scope in normalizedScopes)
        {
            var scopeEntity = new AuthRoleDataScope
            {
                RoleId = role.Id,
                Resource = scope.Resource,
                ScopeType = scope.ScopeType,
                CreatedAt = DateTime.UtcNow
            };
            await _dbContext.AuthRoleDataScopes.AddAsync(scopeEntity, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            foreach (var nodeId in scope.OrgUnitIds)
            {
                await _dbContext.AuthRoleDataScopeNodes.AddAsync(new AuthRoleDataScopeNode
                {
                    RoleDataScopeId = scopeEntity.Id,
                    OrgUnitId = nodeId
                }, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return null;
    }

    private async Task TouchUsersByRoleAsync(int roleId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await _dbContext.SystemUsers
            .Where(user => user.UserRoles.Any(userRole => userRole.RoleId == roleId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(user => user.PermissionVersion, user => user.PermissionVersion + 1)
                .SetProperty(user => user.UpdatedAt, _ => now),
                cancellationToken);
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
