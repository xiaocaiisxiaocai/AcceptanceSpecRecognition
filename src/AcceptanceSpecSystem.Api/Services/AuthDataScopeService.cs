using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 数据范围结果
/// </summary>
public sealed class DataScopeResult
{
    public int UserId { get; init; }

    public int CompanyId { get; init; }

    public int? OrgUnitId { get; init; }

    public bool IsAll { get; init; }

    public bool IncludeSelf { get; init; }

    public IReadOnlyCollection<int> OrgUnitIds { get; init; } = [];
}

/// <summary>
/// 数据范围服务
/// </summary>
public interface IAuthDataScopeService
{
    /// <summary>
    /// 解析用户在指定资源下的数据范围；无角色或无授权范围时返回空范围，避免默认放大权限。
    /// </summary>
    Task<DataScopeResult?> GetScopeAsync(int userId, int companyId, string resource);
}

/// <summary>
/// 数据范围服务实现
/// </summary>
public sealed class AuthDataScopeService : IAuthDataScopeService
{
    private static readonly TimeSpan OrgTreeCacheDuration = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _dbContext;
    private readonly IMemoryCache _memoryCache;

    public AuthDataScopeService(AppDbContext dbContext, IMemoryCache memoryCache)
    {
        _dbContext = dbContext;
        _memoryCache = memoryCache;
    }

    /// <summary>
    /// 解析用户在指定资源下的数据范围；无角色或无授权范围时返回空范围，避免默认放大权限。
    /// </summary>
    public async Task<DataScopeResult?> GetScopeAsync(int userId, int companyId, string resource)
    {
        var normalizedResource = string.IsNullOrWhiteSpace(resource)
            ? "spec"
            : resource.Trim().ToLowerInvariant();
        return await ResolveScopeCoreAsync(userId, companyId, normalizedResource);
    }

    private async Task<DataScopeResult?> ResolveScopeCoreAsync(int userId, int companyId, string normalizedResource)
    {
        var now = DateTime.UtcNow;

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

        var activeOrgLink = AuthUserOrgUnitSingleOrgPolicy.SelectOrgUnitToKeep(activeOrgLinks);
        var orgUnitId = activeOrgLink?.OrgUnitId;
        var userOrgUnitIds = orgUnitId.HasValue
            ? new HashSet<int> { orgUnitId.Value }
            : [];

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
                OrgUnitId = orgUnitId,
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
                OrgUnitId = orgUnitId,
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
                OrgUnitId = orgUnitId,
                IsAll = true,
                IncludeSelf = true,
                OrgUnitIds = []
            };
        }

        var includeSelf = scopes.Any(s => s.ScopeType == DataScopeType.Self);
        var collectedOrgUnitIds = new HashSet<int>();

        var allOrgUnits = await GetCompanyOrgUnitsAsync(companyId);

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
                        // 预处理：将 allOrgUnits 转换为 id→祖先集合的映射，避免 O(N×M) Path.Contains 字符串搜索
                        var idToAncestors = new Dictionary<int, HashSet<int>>(allOrgUnits.Count);
                        foreach (var org in allOrgUnits)
                        {
                            var ancestors = new HashSet<int>();
                            if (!string.IsNullOrWhiteSpace(org.Path))
                            {
                                foreach (var seg in org.Path.Split('/', StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (int.TryParse(seg, out var ancestorId))
                                    {
                                        ancestors.Add(ancestorId);
                                    }
                                }
                            }

                            idToAncestors[org.Id] = ancestors;
                        }

                        var rootNodeIds = scope.Nodes.Count == 0
                            ? userOrgUnitIds
                            : scope.Nodes.Select(n => n.OrgUnitId).Distinct().ToHashSet();
                        foreach (var org in allOrgUnits)
                        {
                            if (idToAncestors.TryGetValue(org.Id, out var ancestors) &&
                                rootNodeIds.Overlaps(ancestors))
                            {
                                collectedOrgUnitIds.Add(org.Id);
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
            OrgUnitId = orgUnitId,
            IsAll = false,
            IncludeSelf = includeSelf,
            OrgUnitIds = collectedOrgUnitIds.ToArray()
        };
    }

    private async Task<IReadOnlyList<CachedOrgUnitNode>> GetCompanyOrgUnitsAsync(int companyId)
    {
        // 组织树缓存按公司维度缓存，并用数量与最后更新时间组成版本戳，
        // 避免每次数据范围计算都扫描组织表。
        var stamp = await _dbContext.OrgUnits
            .AsNoTracking()
            .Where(org => org.CompanyId == companyId)
            .GroupBy(_ => 1)
            .Select(group => new OrgUnitTreeCacheStamp(
                group.Count(),
                group.Max(org => org.UpdatedAt ?? org.CreatedAt)))
            .FirstOrDefaultAsync()
            ?? new OrgUnitTreeCacheStamp(0, DateTime.MinValue);

        var cacheKey = $"auth-org-tree:{companyId}:{stamp.Count}:{stamp.LastChangedAtUtc.Ticks}";
        if (_memoryCache.TryGetValue<IReadOnlyList<CachedOrgUnitNode>>(cacheKey, out var cached) &&
            cached != null)
        {
            return cached;
        }

        var orgUnits = await _dbContext.OrgUnits
            .AsNoTracking()
            .Where(org => org.CompanyId == companyId)
            .Select(org => new CachedOrgUnitNode(org.Id, org.Path))
            .ToListAsync();

        _memoryCache.Set(cacheKey, orgUnits, OrgTreeCacheDuration);
        return orgUnits;
    }

    private sealed record CachedOrgUnitNode(int Id, string Path);

    private sealed record OrgUnitTreeCacheStamp(int Count, DateTime LastChangedAtUtc);
}
