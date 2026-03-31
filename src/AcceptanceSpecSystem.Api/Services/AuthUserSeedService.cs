using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.Controllers;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 鉴权基础数据初始化（公司/组织/角色/权限/默认账号）
/// </summary>
public static class AuthUserSeedService
{
    public const string DefaultCompanyCode = "default-company";
    public const string DefaultCompanyName = "默认公司";
    public const string DefaultRootOrgCode = "ROOT";
    public const string DefaultRootOrgName = "公司";
    public const string DefaultAdminUsername = "admin";
    public const string DefaultCommonUsername = "common";
    public const string DevelopmentDefaultAdminPassword = "admin";
    private const string NavigationManifestRelativePath = "..\\..\\shared\\navigation\\navigation-manifest.json";

    private sealed record PermissionSeedItem(
        string Code,
        string Name,
        PermissionType PermissionType,
        string Resource,
        string Action,
        string? RoutePath = null,
        string? HttpMethod = null,
        string? ApiPath = null);

    private sealed record ApiActionSeedItem(
        string ControllerName,
        string ActionName,
        string RouteTemplate,
        string HttpMethod,
        string? ResourceOverride = null,
        string? ActionOverride = null);

    private sealed class NavigationManifest
    {
        public List<NavigationPermissionItem> Menus { get; set; } = [];

        public List<NavigationPermissionItem> Pages { get; set; } = [];
    }

    private sealed class NavigationPermissionItem
    {
        public string Id { get; set; } = string.Empty;

        public string Code { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Resource { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;
    }

    public static async Task EnsureSeedUsersAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<IAuthPasswordService>();
        var hostEnvironment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var seedOptions = scope.ServiceProvider.GetRequiredService<IOptions<AuthSeedOptions>>().Value;
        var navigationManifest = LoadNavigationManifest(hostEnvironment);

        var now = DateTime.UtcNow;

        var company = await EnsureCompanyAsync(dbContext, now);
        var rootOrgUnit = await EnsureRootOrgUnitAsync(dbContext, company.Id, now);

        var permissionMap = await EnsurePermissionsAsync(dbContext, navigationManifest, now);
        var roleMap = await EnsureRolesAsync(dbContext, company.Id, permissionMap, now);

        await EnsureSeedAccountsAsync(
            dbContext,
            passwordService,
            company.Id,
            roleMap,
            rootOrgUnit.Id,
            now,
            seedOptions,
            hostEnvironment,
            logger);
        await EnsureExistingUserRelationsAsync(dbContext, company.Id, roleMap["common"], rootOrgUnit.Id, now);

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

        logger.LogInformation("鉴权基础数据初始化完成：CompanyId={CompanyId}, RootOrgUnitId={RootOrgUnitId}", company.Id, rootOrgUnit.Id);
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

    private static async Task<Dictionary<string, AuthPermission>> EnsurePermissionsAsync(
        AppDbContext dbContext,
        NavigationManifest navigationManifest,
        DateTime now)
    {
        var permissionSeeds = new Dictionary<string, PermissionSeedItem>(StringComparer.OrdinalIgnoreCase);
        var seededPermissions = new Dictionary<string, AuthPermission>(StringComparer.OrdinalIgnoreCase);

        foreach (var pagePermission in navigationManifest.Pages)
        {
            permissionSeeds[pagePermission.Code] = new PermissionSeedItem(
                Code: pagePermission.Code,
                Name: $"页面-{pagePermission.Title}",
                PermissionType: PermissionType.Page,
                Resource: pagePermission.Resource,
                Action: pagePermission.Action,
                RoutePath: pagePermission.Path);
        }

        foreach (var menuPermission in navigationManifest.Menus)
        {
            permissionSeeds[menuPermission.Code] = new PermissionSeedItem(
                Code: menuPermission.Code,
                Name: $"菜单-{menuPermission.Title}",
                PermissionType: PermissionType.Menu,
                Resource: menuPermission.Resource,
                Action: menuPermission.Action,
                RoutePath: menuPermission.Path);
        }

        var apiActionSeeds = BuildApiActionSeeds();
        foreach (var apiAction in apiActionSeeds)
        {
            var apiPermissionCode = PermissionConventions.ResolveApiPermissionCode(
                controllerName: apiAction.ControllerName,
                actionName: apiAction.ActionName,
                routeTemplate: apiAction.RouteTemplate,
                httpMethod: apiAction.HttpMethod,
                resourceOverride: apiAction.ResourceOverride,
                actionOverride: apiAction.ActionOverride);
            var resource = apiPermissionCode.Split(':', StringSplitOptions.TrimEntries)[1];
            var action = apiPermissionCode.Split(':', StringSplitOptions.TrimEntries)[2];
            var method = apiAction.HttpMethod;
            var routePath = "/" + apiAction.RouteTemplate.Trim('/');

            permissionSeeds[apiPermissionCode] = new PermissionSeedItem(
                Code: apiPermissionCode,
                Name: $"接口-{resource}-{action}",
                PermissionType: PermissionType.Api,
                Resource: resource,
                Action: action,
                ApiPath: routePath,
                HttpMethod: method);

            if (!string.Equals(action, "read", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(action, "login", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(action, "refresh-token", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(action, "routes", StringComparison.OrdinalIgnoreCase))
            {
                var buttonCode = PermissionConventions.BuildButtonPermissionCode(apiPermissionCode);
                permissionSeeds[buttonCode] = new PermissionSeedItem(
                    Code: buttonCode,
                    Name: $"按钮-{resource}-{action}",
                    PermissionType: PermissionType.Button,
                    Resource: resource,
                    Action: action);
            }
        }

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
        return seededPermissions;
    }

    private static NavigationManifest LoadNavigationManifest(IHostEnvironment hostEnvironment)
    {
        var manifestPath = ResolveNavigationManifestPath(hostEnvironment);

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<NavigationManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (manifest == null)
        {
            throw new InvalidOperationException($"导航清单解析失败: {manifestPath}");
        }

        return manifest;
    }

    private static string ResolveNavigationManifestPath(IHostEnvironment hostEnvironment)
    {
        var candidateRoots = new[]
        {
            hostEnvironment.ContentRootPath,
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var root in candidateRoots)
        {
            var directPath = Path.GetFullPath(Path.Combine(root, NavigationManifestRelativePath));
            if (File.Exists(directPath))
            {
                return directPath;
            }

            var current = new DirectoryInfo(root);
            while (current is not null)
            {
                var sharedPath = Path.Combine(current.FullName, "shared", "navigation", "navigation-manifest.json");
                if (File.Exists(sharedPath))
                {
                    return sharedPath;
                }

                current = current.Parent;
            }
        }

        var expectedPath = Path.GetFullPath(Path.Combine(
            hostEnvironment.ContentRootPath,
            NavigationManifestRelativePath));
        throw new InvalidOperationException($"未找到导航清单文件: {expectedPath}");
    }

    private static List<ApiActionSeedItem> BuildApiActionSeeds()
    {
        var controllerTypes = typeof(Program).Assembly.GetTypes()
            .Where(type =>
                !type.IsAbstract &&
                typeof(ControllerBase).IsAssignableFrom(type))
            .ToList();

        var seeds = new List<ApiActionSeedItem>();

        foreach (var controllerType in controllerTypes)
        {
            var controllerName = controllerType.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)
                ? controllerType.Name[..^10]
                : controllerType.Name;

            var controllerRoute = ResolveControllerRouteTemplate(controllerType);

            var actionMethods = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Where(method => method.GetCustomAttribute<NonActionAttribute>(inherit: true) == null)
                .ToList();

            foreach (var method in actionMethods)
            {
                var auditOperation = method.GetCustomAttributes(typeof(AuditOperationAttribute), true)
                    .OfType<AuditOperationAttribute>()
                    .FirstOrDefault();

                var httpMethodAttributes = method.GetCustomAttributes(inherit: true)
                    .OfType<HttpMethodAttribute>()
                    .ToList();
                if (httpMethodAttributes.Count == 0)
                    continue;

                foreach (var httpMethodAttribute in httpMethodAttributes)
                {
                    var routeTemplate = CombineRouteTemplate(
                        controllerTemplate: controllerRoute,
                        actionTemplate: httpMethodAttribute.Template,
                        controllerName: controllerName,
                        actionName: method.Name);
                    var normalizedRouteTemplate = string.IsNullOrWhiteSpace(routeTemplate) ? "/" : routeTemplate;

                    var httpMethods = httpMethodAttribute.HttpMethods
                        .Where(httpMethod => !string.IsNullOrWhiteSpace(httpMethod))
                        .Select(httpMethod => httpMethod.ToUpperInvariant())
                        .DefaultIfEmpty("GET")
                        .ToList();

                    foreach (var httpMethod in httpMethods)
                    {
                        seeds.Add(new ApiActionSeedItem(
                            ControllerName: controllerName,
                            ActionName: method.Name,
                            RouteTemplate: normalizedRouteTemplate,
                            HttpMethod: httpMethod,
                            ResourceOverride: auditOperation?.Resource,
                            ActionOverride: auditOperation?.Operation));
                    }
                }
            }
        }

        return seeds
            .DistinctBy(seed => $"{seed.ControllerName}.{seed.ActionName}.{seed.HttpMethod}.{seed.RouteTemplate}")
            .ToList();
    }

    private static string ResolveControllerRouteTemplate(Type controllerType)
    {
        var directRoute = controllerType
            .GetCustomAttributes<RouteAttribute>(inherit: false)
            .FirstOrDefault();
        if (directRoute != null)
        {
            return directRoute.Template ?? string.Empty;
        }

        var baseType = controllerType.BaseType;
        while (baseType != null)
        {
            var route = baseType
                .GetCustomAttributes<RouteAttribute>(inherit: false)
                .FirstOrDefault();
            if (route != null)
            {
                return route.Template ?? string.Empty;
            }
            baseType = baseType.BaseType;
        }

        return string.Empty;
    }

    private static string CombineRouteTemplate(
        string controllerTemplate,
        string? actionTemplate,
        string controllerName,
        string actionName)
    {
        var normalizedControllerTemplate = ReplaceRouteTokens(controllerTemplate, controllerName, actionName);
        var normalizedActionTemplate = ReplaceRouteTokens(actionTemplate ?? string.Empty, controllerName, actionName);

        if (normalizedActionTemplate.StartsWith("~/", StringComparison.Ordinal))
        {
            return normalizedActionTemplate[2..].Trim('/');
        }

        if (normalizedActionTemplate.StartsWith("/", StringComparison.Ordinal))
        {
            return normalizedActionTemplate.Trim('/');
        }

        if (string.IsNullOrWhiteSpace(normalizedActionTemplate))
        {
            return normalizedControllerTemplate.Trim('/');
        }

        if (string.IsNullOrWhiteSpace(normalizedControllerTemplate))
        {
            return normalizedActionTemplate.Trim('/');
        }

        return $"{normalizedControllerTemplate.TrimEnd('/')}/{normalizedActionTemplate.TrimStart('/')}";
    }

    private static string ReplaceRouteTokens(string template, string controllerName, string actionName)
    {
        if (string.IsNullOrWhiteSpace(template))
            return string.Empty;

        var value = template.Trim();
        value = value.Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase);
        value = value.Replace("[action]", actionName, StringComparison.OrdinalIgnoreCase);
        return value;
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
            "menu:data-import",
            "menu:smart-fill",
            "menu:batch-reply",
            "menu:file-compare",
            "api:auth:routes",
            "page:home:dashboard",
            "page:data-import:index",
            "page:smart-fill:index",
            "page:batch-reply:index",
            "page:file-compare:index",
            "api:document:read",
            "api:document:upload",
            "api:document:import",
            "api:excel-document:import",
            "btn:document:upload",
            "btn:document:import",
            "btn:excel-document:import",
            "api:matching:preview",
            "api:matching:preview-batch",
            "api:matching:download",
            "btn:matching:preview",
            "btn:matching:preview-batch",
            "btn:matching:download",
            "api:matching-fill:llm-stream",
            "api:matching-fill:execute",
            "api:matching-fill:execute-batch",
            "btn:matching-fill:llm-stream",
            "btn:matching-fill:execute",
            "btn:matching-fill:execute-batch",
            "api:matching:similarity",
            "api:batch-reply:read",
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
            "api:process:read",
            "api:machine-model:read",
            "api:spec:read"
        };
        var commonPermissionIds = commonPermissionCodes
            .Where(permissionMap.ContainsKey)
            .Select(code => permissionMap[code].Id)
            .Distinct()
            .ToHashSet();

        var adminPermissionsChanged = await SyncRolePermissionsAsync(dbContext, adminRole.Id, allPermissionIds);
        var commonPermissionsChanged = await SyncRolePermissionsAsync(dbContext, commonRole.Id, commonPermissionIds);

        await EnsureRoleDataScopesAsync(dbContext, adminRole.Id, DataScopeType.All, [], now);
        // 普通角色默认按“主组织及其子树”取范围：
        // OrgSubtree + 空节点 表示运行时回退到用户当前有效组织，而不是绑定公司根节点。
        await EnsureRoleDataScopesAsync(dbContext, commonRole.Id, DataScopeType.OrgSubtree, [], now);

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
        DateTime now,
        AuthSeedOptions seedOptions,
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
        await EnsureUserOrgAsync(dbContext, common.Id, rootOrgUnitId, true, now);
    }

    private static SeedPasswords ResolveSeedPasswords(
        AuthSeedOptions options,
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
                "AuthSeed 未配置完整默认密码，当前环境使用临时开发口令。admin={AdminPassword}; common={CommonPassword}",
                adminPassword,
                commonPassword);

            return new SeedPasswords(adminPassword, commonPassword);
        }

        throw new InvalidOperationException(
            $"缺少 {AuthSeedOptions.SectionName}:AdminPassword 或 {AuthSeedOptions.SectionName}:CommonPassword 配置，生产环境禁止使用源码默认口令。");
    }

    private static string BuildDevelopmentPassword(string prefix)
    {
        return $"{prefix}!{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";
    }

    private sealed record SeedPasswords(string AdminPassword, string CommonPassword);

    private static async Task EnsureExistingUserRelationsAsync(
        AppDbContext dbContext,
        int companyId,
        AuthRole fallbackRole,
        int rootOrgUnitId,
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
                user.UserOrgUnits.Add(new AuthUserOrgUnit
                {
                    OrgUnitId = rootOrgUnitId,
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
