using System.Security.Cryptography;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 鉴权基础数据初始化（公司/组织/角色/权限/默认账号）
/// </summary>
public static class AuthUserSeedAppService
{
    private static readonly IReadOnlyDictionary<string, string> PermissionCodeReplacements =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["api:machine-model:models"] = "api:machine-model:read"
        };

    public const string DefaultCompanyCode = "default-company";
    public const string DefaultCompanyName = "默认公司";
    public const string DefaultRootOrgCode = "ROOT";
    public const string DefaultRootOrgName = "公司";
    public const string DefaultOperationalDepartmentCode = "ELECTRICAL_CONTROL";
    public const string DefaultOperationalDepartmentName = "电控工程部";
    public const string DefaultAdminUsername = "admin";
    public const string DefaultCommonUsername = "common";
    public const string DevelopmentDefaultAdminPassword = "admin";
    public static async Task EnsureSeedUsersAsync(
        IServiceProvider services,
        ILogger logger,
        IHostEnvironment hostEnvironment,
        AuthSeedConfiguration seedOptions)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<IAuthPasswordService>();
        var permissionSeeds = scope.ServiceProvider.GetRequiredService<IAuthPermissionSeedCatalog>().GetSeeds();

        var now = DateTime.UtcNow;

        var company = await EnsureCompanyAsync(dbContext, now);
        var rootOrgUnit = await EnsureRootOrgUnitAsync(dbContext, company.Id, now);
        var operationalDepartment = await EnsureOperationalDepartmentAsync(
            dbContext,
            company.Id,
            rootOrgUnit,
            now);

        var permissionMap = await EnsurePermissionsAsync(dbContext, permissionSeeds, now);
        var roleMap = await EnsureRolesAsync(dbContext, company.Id, permissionMap, now);

        await EnsureSeedAccountsAsync(
            dbContext,
            passwordService,
            company.Id,
            roleMap,
            rootOrgUnit.Id,
            operationalDepartment.Id,
            now,
            seedOptions,
            hostEnvironment,
            logger);
        await EnsureExistingUserRelationsAsync(
            dbContext,
            company.Id,
            roleMap["admin"],
            roleMap["common"],
            rootOrgUnit.Id,
            operationalDepartment.Id,
            now);

        await dbContext.SaveChangesAsync();

        var adminRoleId = roleMap["admin"].Id;
        var adminPermissionStats = await dbContext.AuthRolePermissions
            .AsNoTracking()
            .Where(rp => rp.RoleId == adminRoleId)
            .Join(
                dbContext.AuthPermissions.AsNoTracking(),
                rp => rp.PermissionId,
                p => p.Id,
                (_, permission) => permission.PermissionType)
            .GroupBy(permissionType => permissionType)
            .Select(group => new
            {
                PermissionType = group.Key,
                Count = group.Count()
            })
            .ToListAsync();

        var adminTotalPermissionCount = adminPermissionStats.Sum(x => x.Count);
        var adminPagePermissionCount = adminPermissionStats
            .Where(x => x.PermissionType == PermissionType.Page)
            .Select(x => x.Count)
            .FirstOrDefault();
        var adminButtonPermissionCount = adminPermissionStats
            .Where(x => x.PermissionType == PermissionType.Button)
            .Select(x => x.Count)
            .FirstOrDefault();
        var adminApiPermissionCount = adminPermissionStats
            .Where(x => x.PermissionType == PermissionType.Api)
            .Select(x => x.Count)
            .FirstOrDefault();
        var adminMenuPermissionCount = adminPermissionStats
            .Where(x => x.PermissionType == PermissionType.Menu)
            .Select(x => x.Count)
            .FirstOrDefault();

        logger.LogInformation(
            "鉴权基础数据初始化完成：CompanyId={CompanyId}, RootOrgUnitId={RootOrgUnitId}, OperationalDepartmentId={OperationalDepartmentId}",
            company.Id,
            rootOrgUnit.Id,
            operationalDepartment.Id);
        logger.LogInformation(
            "RBAC权限自检：admin权限总数={Total}, 页面={Page}, 按钮={Button}, API={Api}, 菜单={Menu}",
            adminTotalPermissionCount,
            adminPagePermissionCount,
            adminButtonPermissionCount,
            adminApiPermissionCount,
            adminMenuPermissionCount);
    }

    private static async Task<OrgCompany> EnsureCompanyAsync(AppDbContext dbContext, DateTime now)
    {
        var company = await dbContext.OrgCompanies.FirstOrDefaultAsync(c => c.Code == DefaultCompanyCode);
        if (company != null)
            return company;

        company = new OrgCompany
        {
            Code = DefaultCompanyCode,
            Name = DefaultCompanyName,
            IsActive = true,
            CreatedAt = now
        };
        await dbContext.OrgCompanies.AddAsync(company);
        await dbContext.SaveChangesAsync();
        return company;
    }

    private static async Task<OrgUnit> EnsureRootOrgUnitAsync(AppDbContext dbContext, int companyId, DateTime now)
    {
        var rootOrgUnit = await dbContext.OrgUnits
            .FirstOrDefaultAsync(o => o.CompanyId == companyId && o.UnitType == OrgUnitType.Company && o.ParentId == null);
        if (rootOrgUnit != null)
            return rootOrgUnit;

        rootOrgUnit = new OrgUnit
        {
            CompanyId = companyId,
            ParentId = null,
            UnitType = OrgUnitType.Company,
            Code = DefaultRootOrgCode,
            Name = DefaultRootOrgName,
            Path = "/",
            Depth = 0,
            Sort = 0,
            IsActive = true,
            CreatedAt = now
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        await dbContext.OrgUnits.AddAsync(rootOrgUnit);
        await dbContext.SaveChangesAsync();

        rootOrgUnit.Path = $"/{rootOrgUnit.Id}/";
        rootOrgUnit.UpdatedAt = now;
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return rootOrgUnit;
    }

    private static async Task<OrgUnit> EnsureOperationalDepartmentAsync(
        AppDbContext dbContext,
        int companyId,
        OrgUnit rootOrgUnit,
        DateTime now)
    {
        var department = await dbContext.OrgUnits.FirstOrDefaultAsync(org =>
            org.CompanyId == companyId &&
            org.ParentId == rootOrgUnit.Id &&
            (org.Code == DefaultOperationalDepartmentCode ||
             (org.UnitType == OrgUnitType.Department &&
              org.Name == DefaultOperationalDepartmentName)));
        if (department != null)
        {
            department.UnitType = OrgUnitType.Department;
            department.Code = DefaultOperationalDepartmentCode;
            department.Name = DefaultOperationalDepartmentName;
            department.Path = $"{rootOrgUnit.Path}{department.Id}/";
            department.Depth = rootOrgUnit.Depth + 1;
            department.IsActive = true;
            department.UpdatedAt = now;
            await dbContext.SaveChangesAsync();
            return department;
        }

        department = new OrgUnit
        {
            CompanyId = companyId,
            ParentId = rootOrgUnit.Id,
            UnitType = OrgUnitType.Department,
            Code = DefaultOperationalDepartmentCode,
            Name = DefaultOperationalDepartmentName,
            Path = rootOrgUnit.Path,
            Depth = rootOrgUnit.Depth + 1,
            Sort = 10,
            IsActive = true,
            CreatedAt = now
        };
        await dbContext.OrgUnits.AddAsync(department);
        await dbContext.SaveChangesAsync();

        department.Path = $"{rootOrgUnit.Path}{department.Id}/";
        department.UpdatedAt = now;
        await dbContext.SaveChangesAsync();
        return department;
    }

    private static async Task<Dictionary<string, AuthPermission>> EnsurePermissionsAsync(
        AppDbContext dbContext,
        IReadOnlyCollection<AuthPermissionSeedDefinition> seeds,
        DateTime now)
    {
        var permissionSeeds = seeds.ToDictionary(seed => seed.Code, StringComparer.OrdinalIgnoreCase);
        var seededPermissions = new Dictionary<string, AuthPermission>(StringComparer.OrdinalIgnoreCase);

        var existing = await dbContext.AuthPermissions
            .ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var seed in permissionSeeds.Values)
        {
            if (existing.TryGetValue(seed.Code, out var entity))
            {
                entity.Name = seed.Name;
                entity.PermissionType = seed.PermissionType;
                entity.Resource = seed.Resource;
                entity.Action = seed.Action;
                entity.RoutePath = seed.RoutePath;
                entity.HttpMethod = seed.HttpMethod;
                entity.ApiPath = seed.ApiPath;
                entity.IsBuiltIn = true;
                entity.IsActive = true;
                entity.UpdatedAt = now;
            }
            else
            {
                entity = new AuthPermission
                {
                    Code = seed.Code,
                    Name = seed.Name,
                    PermissionType = seed.PermissionType,
                    Resource = seed.Resource,
                    Action = seed.Action,
                    RoutePath = seed.RoutePath,
                    HttpMethod = seed.HttpMethod,
                    ApiPath = seed.ApiPath,
                    IsBuiltIn = true,
                    IsActive = true,
                    CreatedAt = now
                };
                await dbContext.AuthPermissions.AddAsync(entity);
                existing[seed.Code] = entity;
            }

            seededPermissions[seed.Code] = entity;
        }

        foreach (var entity in existing.Values.Where(permission =>
                     permission.IsBuiltIn &&
                     !permissionSeeds.ContainsKey(permission.Code)))
        {
            entity.IsActive = false;
            entity.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync();
        await MigratePermissionAssignmentsAsync(dbContext, existing, seededPermissions, now);
        return seededPermissions;
    }

    private static async Task MigratePermissionAssignmentsAsync(
        AppDbContext dbContext,
        IReadOnlyDictionary<string, AuthPermission> existingPermissions,
        IReadOnlyDictionary<string, AuthPermission> seededPermissions,
        DateTime now)
    {
        foreach (var (legacyCode, replacementCode) in PermissionCodeReplacements)
        {
            if (!existingPermissions.TryGetValue(legacyCode, out var legacyPermission) ||
                !seededPermissions.TryGetValue(replacementCode, out var replacementPermission))
            {
                continue;
            }

            var legacyLinks = await dbContext.AuthRolePermissions
                .Where(link => link.PermissionId == legacyPermission.Id)
                .ToListAsync();
            if (legacyLinks.Count == 0)
                continue;

            var affectedRoleIds = legacyLinks
                .Select(link => link.RoleId)
                .Distinct()
                .ToArray();
            var roleIdsWithReplacement = (await dbContext.AuthRolePermissions
                .Where(link =>
                    affectedRoleIds.Contains(link.RoleId) &&
                    link.PermissionId == replacementPermission.Id)
                .Select(link => link.RoleId)
                .ToListAsync())
                .ToHashSet();

            foreach (var roleId in affectedRoleIds.Where(roleId => !roleIdsWithReplacement.Contains(roleId)))
            {
                await dbContext.AuthRolePermissions.AddAsync(new AuthRolePermission
                {
                    RoleId = roleId,
                    PermissionId = replacementPermission.Id
                });
            }

            dbContext.AuthRolePermissions.RemoveRange(legacyLinks);
            await dbContext.SaveChangesAsync();

            foreach (var roleId in affectedRoleIds)
            {
                await TouchUsersByRoleAsync(dbContext, roleId, now);
            }
        }
    }

    private static async Task<Dictionary<string, AuthRole>> EnsureRolesAsync(
        AppDbContext dbContext,
        int companyId,
        Dictionary<string, AuthPermission> permissionMap,
        DateTime now)
    {
        var roleMap = await dbContext.AuthRoles
            .Where(r => r.CompanyId == companyId && (r.Code == "admin" || r.Code == "common"))
            .ToDictionaryAsync(r => r.Code, StringComparer.OrdinalIgnoreCase);

        if (!roleMap.TryGetValue("admin", out var adminRole))
        {
            adminRole = new AuthRole
            {
                CompanyId = companyId,
                Code = "admin",
                Name = "系统管理员",
                Description = "内置管理员角色，拥有全部权限",
                IsBuiltIn = true,
                IsActive = true,
                CreatedAt = now
            };
            await dbContext.AuthRoles.AddAsync(adminRole);
            roleMap["admin"] = adminRole;
        }

        if (!roleMap.TryGetValue("common", out var commonRole))
        {
            commonRole = new AuthRole
            {
                CompanyId = companyId,
                Code = "common",
                Name = "普通用户",
                Description = "内置普通角色，默认按主组织及其子树控制验收规格范围",
                IsBuiltIn = true,
                IsActive = true,
                CreatedAt = now
            };
            await dbContext.AuthRoles.AddAsync(commonRole);
            roleMap["common"] = commonRole;
        }

        await dbContext.SaveChangesAsync();

        var allPermissionIds = permissionMap.Values.Select(p => p.Id).Distinct().ToHashSet();
        var commonPermissionCodes = new[]
        {
            "menu:home",
            "menu:base-data",
            "menu:data-import",
            "menu:smart-fill",
            "menu:batch-reply",
            "menu:file-compare",
            "menu:rbac",
            "page:home:dashboard",
            "page:base-data:customers",
            "page:base-data:processes",
            "page:base-data:machine-models",
            "page:base-data:specs",
            "page:data-import:index",
            "page:smart-fill:index",
            "page:batch-reply:index",
            "page:file-compare:index",
            "page:config:system-users",
            "api:dashboard:read",
            "api:document:read",
            "api:document:upload",
            "api:document:preview",
            "api:document:import",
            "api:document:delete",
            "api:excel-document:import",
            "api:smart-config:create",
            "btn:smart-config:create",
            "btn:document:upload",
            "btn:document:import",
            "btn:document:delete",
            "btn:excel-document:import",
            "api:matching:read",
            "api:matching:preview-batch",
            "api:matching:download",
            "btn:matching:preview-batch",
            "btn:matching:download",
            "api:matching-fill:llm-stream",
            "api:matching-fill:execute-batch",
            "api:matching-fill:spec-backfill",
            "btn:matching-fill:llm-stream",
            "btn:matching-fill:execute-batch",
            "api:batch-reply:read",
            "api:batch-reply:upload",
            "api:batch-reply:upload-source",
            "api:batch-reply:preview",
            "btn:batch-reply:preview",
            "api:batch-reply:execute",
            "btn:batch-reply:execute",
            "api:batch-reply:download",
            "api:file-compare:upload",
            "api:file-compare:preview",
            "api:file-compare:download",
            "btn:file-compare:upload",
            "btn:file-compare:preview",
            "btn:file-compare:download",
            "api:customer:read",
            "api:customer:create",
            "api:customer:update",
            "api:customer:delete",
            "api:customer:batch-delete",
            "btn:customer:create",
            "btn:customer:update",
            "btn:customer:delete",
            "btn:customer:batch-delete",
            "api:process:read",
            "api:process:create",
            "api:process:update",
            "api:process:delete",
            "api:process:batch-delete",
            "btn:process:create",
            "btn:process:update",
            "btn:process:delete",
            "btn:process:batch-delete",
            "api:machine-model:read",
            "api:machine-model:create",
            "api:machine-model:update",
            "api:machine-model:delete",
            "api:machine-model:batch-delete",
            "btn:machine-model:create",
            "btn:machine-model:update",
            "btn:machine-model:delete",
            "btn:machine-model:batch-delete",
            "api:spec:read",
            "api:spec:create",
            "api:spec:update",
            "api:spec:delete",
            "api:spec:delete-batch",
            "api:spec:semantic-search",
            "api:spec:remark-replace",
            "btn:spec:create",
            "btn:spec:update",
            "btn:spec:delete",
            "btn:spec:delete-batch",
            "btn:spec:semantic-search",
            "btn:spec:remark-replace",
            "api:system-user:read",
            "api:system-user:create",
            "api:system-user:update",
            "api:system-user:update-status",
            "api:system-user:reset-password",
            "api:system-user:delete",
            "btn:system-user:create",
            "btn:system-user:update",
            "btn:system-user:update-status",
            "btn:system-user:reset-password",
            "btn:system-user:delete",
            "api:auth-role:read",
            "api:org-unit:read",
            "api:ai-service:read"
        };
        var commonPermissionIds = commonPermissionCodes
            .Where(permissionMap.ContainsKey)
            .Select(code => permissionMap[code].Id)
            .Distinct()
            .ToHashSet();

        var adminPermissionsChanged = await SyncRolePermissionsAsync(dbContext, adminRole.Id, allPermissionIds);
        // common 在首次初始化前由种子维护；管理员通过角色管理保存后会写入 UpdatedAt，
        // 此后必须保留人工配置，避免应用重启重新覆盖权限和数据范围。
        var commonManagedBySeed = commonRole.UpdatedAt == null;
        var commonPermissionsChanged = commonManagedBySeed &&
                                       await SyncRolePermissionsAsync(dbContext, commonRole.Id, commonPermissionIds);

        await EnsureRoleDataScopesAsync(dbContext, adminRole.Id, DataScopeType.All, [], now);
        // 普通角色默认按“主组织及其子树”取范围：
        // OrgSubtree + 空节点 表示运行时回退到用户当前有效组织，而不是绑定公司根节点。
        if (commonManagedBySeed)
        {
            await EnsureRoleDataScopesAsync(dbContext, commonRole.Id, DataScopeType.OrgSubtree, [], now);
        }

        if (adminPermissionsChanged)
        {
            await TouchUsersByRoleAsync(dbContext, adminRole.Id, now);
        }

        if (commonPermissionsChanged)
        {
            await TouchUsersByRoleAsync(dbContext, commonRole.Id, now);
        }

        return roleMap;
    }

    private static async Task<bool> SyncRolePermissionsAsync(AppDbContext dbContext, int roleId, HashSet<int> expectedPermissionIds)
    {
        var current = await dbContext.AuthRolePermissions
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync();

        var currentIds = current.Select(x => x.PermissionId).ToHashSet();
        var toAdd = expectedPermissionIds.Where(id => !currentIds.Contains(id));
        var toRemove = current.Where(x => !expectedPermissionIds.Contains(x.PermissionId)).ToList();

        foreach (var permissionId in toAdd)
        {
            await dbContext.AuthRolePermissions.AddAsync(new AuthRolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId
            });
        }

        if (toRemove.Count > 0)
        {
            dbContext.AuthRolePermissions.RemoveRange(toRemove);
        }

        var changed = toAdd.Any() || toRemove.Count > 0;
        await dbContext.SaveChangesAsync();
        return changed;
    }

    private static async Task EnsureRoleDataScopesAsync(
        AppDbContext dbContext,
        int roleId,
        DataScopeType scopeType,
        IEnumerable<int> orgUnitIds,
        DateTime now)
    {
        var scope = await dbContext.AuthRoleDataScopes
            .Include(s => s.Nodes)
            .FirstOrDefaultAsync(s => s.RoleId == roleId && s.Resource == "spec");

        if (scope == null)
        {
            scope = new AuthRoleDataScope
            {
                RoleId = roleId,
                Resource = "spec",
                ScopeType = scopeType,
                CreatedAt = now
            };
            await dbContext.AuthRoleDataScopes.AddAsync(scope);
            await dbContext.SaveChangesAsync();
        }
        else
        {
            scope.ScopeType = scopeType;
        }

        var targetNodeIds = orgUnitIds.Distinct().ToHashSet();
        var currentNodeIds = scope.Nodes.Select(n => n.OrgUnitId).ToHashSet();
        var addNodes = targetNodeIds.Where(id => !currentNodeIds.Contains(id));
        var removeNodes = scope.Nodes.Where(n => !targetNodeIds.Contains(n.OrgUnitId)).ToList();

        foreach (var nodeId in addNodes)
        {
            await dbContext.AuthRoleDataScopeNodes.AddAsync(new AuthRoleDataScopeNode
            {
                RoleDataScopeId = scope.Id,
                OrgUnitId = nodeId
            });
        }

        if (removeNodes.Count > 0)
        {
            dbContext.AuthRoleDataScopeNodes.RemoveRange(removeNodes);
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task EnsureSeedAccountsAsync(
        AppDbContext dbContext,
        IAuthPasswordService passwordService,
        int companyId,
        Dictionary<string, AuthRole> roleMap,
        int rootOrgUnitId,
        int operationalDepartmentId,
        DateTime now,
        AuthSeedConfiguration seedOptions,
        IHostEnvironment hostEnvironment,
        ILogger logger)
    {
        var admin = await dbContext.SystemUsers.FirstOrDefaultAsync(u => u.Username == DefaultAdminUsername);
        var common = await dbContext.SystemUsers.FirstOrDefaultAsync(u => u.Username == DefaultCommonUsername);
        SeedPasswords? seedPasswords = null;

        if (admin == null)
        {
            seedPasswords ??= ResolveSeedPasswords(seedOptions, hostEnvironment, logger);
            admin = new SystemUser
            {
                CompanyId = companyId,
                Username = DefaultAdminUsername,
                PasswordHash = passwordService.HashPassword(seedPasswords.AdminPassword),
                Nickname = "管理员",
                Avatar = "https://avatars.githubusercontent.com/u/44761321",
                IsActive = true,
                PermissionVersion = 1,
                CreatedAt = now
            };
            await dbContext.SystemUsers.AddAsync(admin);
            await dbContext.SaveChangesAsync();
        }

        if (common == null)
        {
            seedPasswords ??= ResolveSeedPasswords(seedOptions, hostEnvironment, logger);
            common = new SystemUser
            {
                CompanyId = companyId,
                Username = DefaultCommonUsername,
                PasswordHash = passwordService.HashPassword(seedPasswords.CommonPassword),
                Nickname = "普通用户",
                Avatar = "https://avatars.githubusercontent.com/u/52823142",
                IsActive = true,
                PermissionVersion = 1,
                CreatedAt = now
            };
            await dbContext.SystemUsers.AddAsync(common);
            await dbContext.SaveChangesAsync();
        }

        await EnsureUserRoleAsync(dbContext, admin.Id, roleMap["admin"].Id, now);
        await EnsureUserOrgAsync(dbContext, admin.Id, rootOrgUnitId, true, now);

        await EnsureUserRoleAsync(dbContext, common.Id, roleMap["common"].Id, now);
        await EnsureUserOrgAsync(dbContext, common.Id, operationalDepartmentId, true, now);
    }

    private static SeedPasswords ResolveSeedPasswords(
        AuthSeedConfiguration options,
        IHostEnvironment hostEnvironment,
        ILogger logger)
    {
        var adminPassword = options.AdminPassword?.Trim();
        var commonPassword = options.CommonPassword?.Trim();

        if (!string.IsNullOrWhiteSpace(adminPassword) && !string.IsNullOrWhiteSpace(commonPassword))
        {
            return new SeedPasswords(adminPassword, commonPassword);
        }

        if (hostEnvironment.IsDevelopment() || hostEnvironment.IsEnvironment("Testing"))
        {
            adminPassword ??= DevelopmentDefaultAdminPassword;
            commonPassword ??= BuildDevelopmentPassword("Common");

            logger.LogWarning(
                "AuthSeed 未配置完整默认密码，当前环境使用临时开发口令。请通过配置显式设置 {AdminPasswordKey} 与 {CommonPasswordKey}，日志中不输出明文口令。",
                $"{AuthSeedConfiguration.SectionName}:AdminPassword",
                $"{AuthSeedConfiguration.SectionName}:CommonPassword");

            return new SeedPasswords(adminPassword, commonPassword);
        }

        throw new InvalidOperationException(
            $"缺少 {AuthSeedConfiguration.SectionName}:AdminPassword 或 {AuthSeedConfiguration.SectionName}:CommonPassword 配置，生产环境禁止使用源码默认口令。");
    }

    private static string BuildDevelopmentPassword(string prefix)
    {
        return $"{prefix}!{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";
    }

    private sealed record SeedPasswords(string AdminPassword, string CommonPassword);

    private static async Task EnsureExistingUserRelationsAsync(
        AppDbContext dbContext,
        int companyId,
        AuthRole adminRole,
        AuthRole fallbackRole,
        int rootOrgUnitId,
        int operationalDepartmentId,
        DateTime now)
    {
        var users = await dbContext.SystemUsers
            .AsSplitQuery()
            .Include(u => u.UserRoles)
            .Include(u => u.UserOrgUnits)
            .ToListAsync();

        foreach (var user in users)
        {
            var changed = false;
            if (user.CompanyId <= 0)
            {
                user.CompanyId = companyId;
                changed = true;
            }

            if (user.UserRoles.Count == 0)
            {
                user.UserRoles.Add(new AuthUserRole
                {
                    RoleId = fallbackRole.Id,
                    StartAt = null,
                    EndAt = null,
                    CreatedAt = now
                });
                changed = true;
            }

            if (user.UserOrgUnits.Count == 0)
            {
                var isAdministrator = user.UserRoles.Any(userRole => userRole.RoleId == adminRole.Id);
                user.UserOrgUnits.Add(new AuthUserOrgUnit
                {
                    OrgUnitId = isAdministrator ? rootOrgUnitId : operationalDepartmentId,
                    IsPrimary = true,
                    StartAt = null,
                    EndAt = null,
                    CreatedAt = now
                });
                changed = true;
            }
            else
            {
                var orgToKeep = AuthUserOrgUnitSingleOrgPolicy.SelectOrgUnitToKeep(user.UserOrgUnits);
                var extraOrgLinks = user.UserOrgUnits
                    .Where(link => orgToKeep == null || link.Id != orgToKeep.Id)
                    .ToList();
                if (extraOrgLinks.Count > 0)
                {
                    dbContext.AuthUserOrgUnits.RemoveRange(extraOrgLinks);
                    changed = true;
                }

                if (orgToKeep != null && !orgToKeep.IsPrimary)
                {
                    orgToKeep.IsPrimary = true;
                    changed = true;
                }
            }

            if (changed)
            {
                user.PermissionVersion += 1;
                user.UpdatedAt = now;
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task EnsureUserRoleAsync(AppDbContext dbContext, int userId, int roleId, DateTime now)
    {
        var exists = await dbContext.AuthUserRoles.AnyAsync(x => x.UserId == userId && x.RoleId == roleId);
        if (exists)
            return;

        await dbContext.AuthUserRoles.AddAsync(new AuthUserRole
        {
            UserId = userId,
            RoleId = roleId,
            StartAt = null,
            EndAt = null,
            CreatedAt = now
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task EnsureUserOrgAsync(AppDbContext dbContext, int userId, int orgUnitId, bool isPrimary, DateTime now)
    {
        var orgLinks = await dbContext.AuthUserOrgUnits
            .Where(x => x.UserId == userId)
            .ToListAsync();

        var current = orgLinks.FirstOrDefault(x => x.OrgUnitId == orgUnitId);
        if (current == null)
        {
            current = new AuthUserOrgUnit
            {
                UserId = userId,
                OrgUnitId = orgUnitId,
                IsPrimary = isPrimary,
                StartAt = null,
                EndAt = null,
                CreatedAt = now
            };
            await dbContext.AuthUserOrgUnits.AddAsync(current);
            orgLinks.Add(current);
        }

        var extraOrgLinks = orgLinks
            .Where(x => x.OrgUnitId != orgUnitId)
            .ToList();
        if (extraOrgLinks.Count > 0)
        {
            dbContext.AuthUserOrgUnits.RemoveRange(extraOrgLinks);
        }

        current.IsPrimary = true;
        await dbContext.SaveChangesAsync();
    }

    private static async Task TouchUsersByRoleAsync(AppDbContext dbContext, int roleId, DateTime now)
    {
        await dbContext.SystemUsers
            .Where(user => user.UserRoles.Any(userRole => userRole.RoleId == roleId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(user => user.PermissionVersion, user => user.PermissionVersion + 1)
                .SetProperty(user => user.UpdatedAt, _ => now));
    }
}
