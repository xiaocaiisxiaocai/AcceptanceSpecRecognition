using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 数据范围结果
/// </summary>
public sealed class DataScopeResult
{
    public int UserId { get; init; }

    public int CompanyId { get; init; }

    public int? PrimaryOrgUnitId { get; init; }

    public bool IsAll { get; init; }

    public bool IncludeSelf { get; init; }

    public IReadOnlyCollection<int> OrgUnitIds { get; init; } = [];
}

/// <summary>
/// 数据范围服务
/// </summary>
public interface IAuthDataScopeService
{
    Task<DataScopeResult?> GetScopeAsync(int userId, int companyId, string resource);
}

/// <summary>
/// 数据范围服务实现
/// </summary>
public sealed class AuthDataScopeService : IAuthDataScopeService
{
    private readonly AppDbContext _dbContext;

    public AuthDataScopeService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DataScopeResult?> GetScopeAsync(int userId, int companyId, string resource)
    {
        var now = DateTime.Now;
        var normalizedResource = string.IsNullOrWhiteSpace(resource)
            ? "spec"
            : resource.Trim().ToLowerInvariant();

        var user = await _dbContext.SystemUsers
            .AsNoTracking()
            .Where(u => u.Id == userId && u.CompanyId == companyId && u.IsActive)
            .Select(u => new
            {
                u.Id,
                u.CompanyId
            })
            .FirstOrDefaultAsync();
        if (user == null)
            return null;

        var activeOrgLinks = await _dbContext.AuthUserOrgUnits
            .AsNoTracking()
            .Include(x => x.OrgUnit)
            .Where(x =>
                x.UserId == userId &&
                x.OrgUnit.CompanyId == companyId &&
                x.OrgUnit.IsActive &&
                (!x.StartAt.HasValue || x.StartAt <= now) &&
                (!x.EndAt.HasValue || x.EndAt >= now))
            .ToListAsync();

        var primaryOrgUnitId = activeOrgLinks
            .Where(x => x.IsPrimary)
            .Select(x => (int?)x.OrgUnitId)
            .FirstOrDefault()
            ?? activeOrgLinks.Select(x => (int?)x.OrgUnitId).FirstOrDefault();

        var userOrgUnitIds = activeOrgLinks
            .Select(x => x.OrgUnitId)
            .Distinct()
            .ToHashSet();

        var activeRoleIds = await _dbContext.AuthUserRoles
            .AsNoTracking()
            .Where(x =>
                x.UserId == userId &&
                x.Role.CompanyId == companyId &&
                x.Role.IsActive &&
                (!x.StartAt.HasValue || x.StartAt <= now) &&
                (!x.EndAt.HasValue || x.EndAt >= now))
            .Select(x => x.RoleId)
            .Distinct()
            .ToListAsync();

        if (activeRoleIds.Count == 0)
        {
            return new DataScopeResult
            {
                UserId = user.Id,
                CompanyId = user.CompanyId,
                PrimaryOrgUnitId = primaryOrgUnitId,
                IsAll = false,
                IncludeSelf = false,
                OrgUnitIds = []
            };
        }

        var scopes = await _dbContext.AuthRoleDataScopes
            .AsNoTracking()
            .Include(s => s.Nodes)
            .Where(s =>
                activeRoleIds.Contains(s.RoleId) &&
                (s.Resource == normalizedResource || s.Resource == "*"))
            .ToListAsync();

        if (scopes.Count == 0)
        {
            return new DataScopeResult
            {
                UserId = user.Id,
                CompanyId = user.CompanyId,
                PrimaryOrgUnitId = primaryOrgUnitId,
                IsAll = false,
                IncludeSelf = false,
                OrgUnitIds = []
            };
        }

        if (scopes.Any(s => s.ScopeType == DataScopeType.All))
        {
            return new DataScopeResult
            {
                UserId = user.Id,
                CompanyId = user.CompanyId,
                PrimaryOrgUnitId = primaryOrgUnitId,
                IsAll = true,
                IncludeSelf = true,
                OrgUnitIds = []
            };
        }

        var includeSelf = scopes.Any(s => s.ScopeType == DataScopeType.Self);
        var collectedOrgUnitIds = new HashSet<int>();

        var allOrgUnits = await _dbContext.OrgUnits
            .AsNoTracking()
            .Where(o => o.CompanyId == companyId)
            .Select(o => new { o.Id, o.Path })
            .ToListAsync();

        foreach (var scope in scopes)
        {
            switch (scope.ScopeType)
            {
                case DataScopeType.OrgNode:
                {
                    var nodeIds = scope.Nodes.Count == 0
                        ? userOrgUnitIds
                        : scope.Nodes.Select(n => n.OrgUnitId).Distinct();
                    foreach (var nodeId in nodeIds)
                    {
                        collectedOrgUnitIds.Add(nodeId);
                    }

                    break;
                }
                case DataScopeType.OrgSubtree:
                {
                    var rootNodeIds = scope.Nodes.Count == 0
                        ? userOrgUnitIds
                        : scope.Nodes.Select(n => n.OrgUnitId).Distinct().ToHashSet();
                    foreach (var rootNodeId in rootNodeIds)
                    {
                        var marker = $"/{rootNodeId}/";
                        foreach (var orgUnit in allOrgUnits)
                        {
                            if (orgUnit.Path.Contains(marker, StringComparison.Ordinal))
                            {
                                collectedOrgUnitIds.Add(orgUnit.Id);
                            }
                        }
                    }

                    break;
                }
                case DataScopeType.CustomNodes:
                {
                    foreach (var nodeId in scope.Nodes.Select(n => n.OrgUnitId))
                    {
                        collectedOrgUnitIds.Add(nodeId);
                    }

                    break;
                }
            }
        }

        return new DataScopeResult
        {
            UserId = user.Id,
            CompanyId = user.CompanyId,
            PrimaryOrgUnitId = primaryOrgUnitId,
            IsAll = false,
            IncludeSelf = includeSelf,
            OrgUnitIds = collectedOrgUnitIds.ToArray()
        };
    }
}
