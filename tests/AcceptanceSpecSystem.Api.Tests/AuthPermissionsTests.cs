using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Api.Tests;

public class AuthPermissionsTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly Regex PermissionCodePattern = new(
        @"^(menu:[a-z0-9-]+|(?:page|btn|api):[a-z0-9-]+:[a-z0-9-]+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HttpClient _client;
    private readonly ApiWebApplicationFactory _factory;

    public AuthPermissionsTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public void PermissionConventions_ShouldNotReserveLegacyMatchingSimilarityPermissionCode()
    {
        var permissionCode = PermissionConventions.ResolveApiPermissionCode(
            controllerName: "Matching",
            actionName: "Similarity",
            routeTemplate: "api/matching/similarity",
            httpMethod: "POST");

        permissionCode.Should().Be("api:matching:create", "历史 similarity 路由已移除，不应继续保留专用权限动作");
    }

    [Fact]
    public void PermissionConventions_ShouldDistinguishMachineModelReadFromAiModelProbe()
    {
        var machineModelRead = PermissionConventions.ResolveApiPermissionCode(
            controllerName: "MachineModels",
            actionName: "GetMachineModels",
            routeTemplate: "api/machine-models",
            httpMethod: "GET");
        var aiModelProbe = PermissionConventions.ResolveApiPermissionCode(
            controllerName: "AiServices",
            actionName: "GetModels",
            routeTemplate: "api/ai-services/{id}/models",
            httpMethod: "GET");

        machineModelRead.Should().Be("api:machine-model:read");
        aiModelProbe.Should().Be("api:ai-service:models");
    }

    [Fact]
    public void PermissionSeedCatalog_ShouldBeCompleteAndWellFormed()
    {
        var seeds = _factory.Services
            .GetRequiredService<IAuthPermissionSeedCatalog>()
            .GetSeeds()
            .ToList();

        seeds.Should().NotBeEmpty();
        seeds.Select(seed => seed.Code).Should().OnlyHaveUniqueItems();
        seeds.Should().OnlyContain(seed =>
            PermissionCodePattern.IsMatch(seed.Code) &&
            !string.IsNullOrWhiteSpace(seed.Name) &&
            !string.IsNullOrWhiteSpace(seed.Resource) &&
            !string.IsNullOrWhiteSpace(seed.Action));

        seeds.Where(seed => seed.PermissionType is PermissionType.Page or PermissionType.Menu)
            .Should().OnlyContain(seed => !string.IsNullOrWhiteSpace(seed.RoutePath));
        seeds.Where(seed => seed.PermissionType == PermissionType.Api)
            .Should().OnlyContain(seed =>
                !string.IsNullOrWhiteSpace(seed.HttpMethod) &&
                !string.IsNullOrWhiteSpace(seed.ApiPath));
        seeds.Where(seed => seed.PermissionType == PermissionType.Page)
            .Should().OnlyContain(seed => seed.Code.StartsWith("page:", StringComparison.Ordinal));
        seeds.Where(seed => seed.PermissionType == PermissionType.Button)
            .Should().OnlyContain(seed => seed.Code.StartsWith("btn:", StringComparison.Ordinal));
        seeds.Where(seed => seed.PermissionType == PermissionType.Api)
            .Should().OnlyContain(seed => seed.Code.StartsWith("api:", StringComparison.Ordinal));
        seeds.Where(seed => seed.PermissionType == PermissionType.Menu)
            .Should().OnlyContain(seed => seed.Code.StartsWith("menu:", StringComparison.Ordinal));
        seeds.Select(seed => seed.Code)
            .Where(code => code.Contains("machine-model", StringComparison.Ordinal))
            .Should().Contain("api:machine-model:read");
    }

    [Fact]
    public void PermissionSeedCatalog_ShouldCoverEveryFrontendPermissionReference()
    {
        var seedCodes = _factory.Services
            .GetRequiredService<IAuthPermissionSeedCatalog>()
            .GetSeeds()
            .Select(seed => seed.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var webSourceRoot = Path.Combine(GetRepositoryRoot(), "web", "src");
        var referencePattern = new Regex(
            @"(?:api|btn):[a-z0-9-]+:[a-z0-9-]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        var referencedCodes = Directory
            .EnumerateFiles(webSourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".ts" or ".vue")
            .SelectMany(path => referencePattern.Matches(File.ReadAllText(path)))
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        referencedCodes.Except(seedCodes, StringComparer.OrdinalIgnoreCase)
            .Should().BeEmpty("前端引用的每个按钮/API权限都必须存在于权限字典");
    }

    [Fact]
    public void PermissionSeedCatalog_ShouldCoverEveryProtectedControllerAction()
    {
        var expectedApiCodes = _factory.Services
            .GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(descriptor => !descriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            .Select(PermissionConventions.ResolveApiPermissionCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualApiCodes = _factory.Services
            .GetRequiredService<IAuthPermissionSeedCatalog>()
            .GetSeeds()
            .Where(seed => seed.PermissionType == PermissionType.Api)
            .Select(seed => seed.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        actualApiCodes.Should().BeEquivalentTo(expectedApiCodes,
            "权限字典必须覆盖权限中间件实际保护的全部控制器动作");
    }

    [Fact]
    public void NavigationManifest_ShouldExactlyMatchFrontendRoutePermissionReferences()
    {
        var repositoryRoot = GetRepositoryRoot();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repositoryRoot, "shared", "navigation", "navigation-manifest.json")));
        var root = manifest.RootElement;
        var menuItems = root.GetProperty("menus").EnumerateArray().ToList();
        var pageItems = root.GetProperty("pages").EnumerateArray().ToList();
        var menuIds = menuItems.Select(item => item.GetProperty("id").GetString()!).ToList();
        var pageIds = pageItems.Select(item => item.GetProperty("id").GetString()!).ToList();
        var allCodes = menuItems.Concat(pageItems)
            .Select(item => item.GetProperty("code").GetString()!)
            .ToList();
        var routeSource = string.Join('\n', Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "web", "src", "router", "modules"), "*.ts")
            .Select(File.ReadAllText));
        var menuReferences = Regex.Matches(routeSource, @"getMenuPermission\(""([^""]+)""\)")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pageReferences = Regex.Matches(routeSource, @"getPagePermission\(""([^""]+)""\)")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        menuIds.Should().OnlyHaveUniqueItems();
        pageIds.Should().OnlyHaveUniqueItems();
        allCodes.Should().OnlyHaveUniqueItems();
        menuReferences.Should().BeEquivalentTo(menuIds);
        pageReferences.Should().BeEquivalentTo(pageIds);
        menuItems.Concat(pageItems).Should().OnlyContain(item =>
            !string.IsNullOrWhiteSpace(item.GetProperty("title").GetString()) &&
            !string.IsNullOrWhiteSpace(item.GetProperty("resource").GetString()) &&
            !string.IsNullOrWhiteSpace(item.GetProperty("action").GetString()) &&
            !string.IsNullOrWhiteSpace(item.GetProperty("path").GetString()));
    }

    [Fact]
    public void PermissionSeedCatalog_ShouldNotExposeAnonymousEndpointsAsPermissions()
    {
        var permissionCodes = _factory.Services
            .GetRequiredService<IAuthPermissionSeedCatalog>()
            .GetSeeds()
            .Select(seed => seed.Code)
            .ToList();

        permissionCodes.Should().NotContain("api:auth:login");
        permissionCodes.Should().NotContain("api:auth:refresh-token");
        permissionCodes.Should().NotContain("api:auth:logout");
        permissionCodes.Should().NotContain("btn:auth:logout");
    }

    [Fact]
    public async Task Seed_ShouldSynchronizeActivePermissionDictionaryWithCatalog()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        var expectedCodes = _factory.Services
            .GetRequiredService<IAuthPermissionSeedCatalog>()
            .GetSeeds()
            .Select(seed => seed.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var activeCodes = await dbContext.AuthPermissions
            .AsNoTracking()
            .Where(permission => permission.IsActive)
            .Select(permission => permission.Code)
            .ToListAsync();

        activeCodes.Should().BeEquivalentTo(expectedCodes);
    }

    [Fact]
    public async Task Seed_ShouldDeactivateObsoleteAnonymousAndMisclassifiedPermissions()
    {
        var obsoleteCodes = new[]
        {
            "api:auth:login",
            "api:auth:refresh-token",
            "api:auth:logout",
            "btn:auth:logout",
            "api:machine-model:models",
            "btn:machine-model:models"
        };
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var code in obsoleteCodes)
            {
                var permission = await dbContext.AuthPermissions
                    .SingleOrDefaultAsync(item => item.Code == code);
                if (permission is null)
                {
                    permission = new AuthPermission
                    {
                        Code = code,
                        Name = $"旧权限-{code}",
                        PermissionType = code.StartsWith("btn:", StringComparison.Ordinal)
                            ? PermissionType.Button
                            : PermissionType.Api,
                        Resource = "legacy",
                        Action = "legacy",
                        IsBuiltIn = true,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    dbContext.AuthPermissions.Add(permission);
                }
                else
                {
                    permission.IsBuiltIn = true;
                    permission.IsActive = true;
                }
            }

            await dbContext.SaveChangesAsync();
        }

        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var obsoletePermissions = await verifyDbContext.AuthPermissions
            .Where(permission => obsoleteCodes.Contains(permission.Code))
            .ToListAsync();
        obsoletePermissions.Should().HaveCount(obsoleteCodes.Length);
        obsoletePermissions.Should().OnlyContain(permission => !permission.IsActive);
    }

    [Fact]
    public async Task Seed_ShouldMigrateCustomRoleFromMisclassifiedMachineModelPermission()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        var suffix = Guid.NewGuid().ToString("N");
        var roleCode = $"permission-migration-{suffix}";
        var username = $"permission-migration-{suffix}";
        int roleId;
        int userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var companyId = await dbContext.AuthRoles
                .Where(role => role.Code == "admin")
                .Select(role => role.CompanyId)
                .SingleAsync();
            var legacyPermission = await dbContext.AuthPermissions
                .SingleOrDefaultAsync(permission => permission.Code == "api:machine-model:models");
            if (legacyPermission is null)
            {
                legacyPermission = new AuthPermission
                {
                    Code = "api:machine-model:models",
                    Name = "旧权限-api:machine-model:models",
                    PermissionType = PermissionType.Api,
                    Resource = "machine-model",
                    Action = "models",
                    IsBuiltIn = true,
                    IsActive = false,
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.AuthPermissions.Add(legacyPermission);
            }

            var role = new AuthRole
            {
                CompanyId = companyId,
                Code = roleCode,
                Name = "权限迁移测试角色",
                Description = "验证旧机型权限迁移",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            var user = new SystemUser
            {
                CompanyId = companyId,
                Username = username,
                PasswordHash = "test-password-hash",
                Nickname = "权限迁移测试用户",
                IsActive = true,
                PermissionVersion = 1,
                CreatedAt = DateTime.UtcNow
            };
            dbContext.AuthRoles.Add(role);
            dbContext.SystemUsers.Add(user);
            await dbContext.SaveChangesAsync();

            roleId = role.Id;
            userId = user.Id;
            dbContext.AuthRolePermissions.Add(new AuthRolePermission
            {
                RoleId = roleId,
                PermissionId = legacyPermission.Id
            });
            dbContext.AuthUserRoles.Add(new AuthUserRole
            {
                UserId = userId,
                RoleId = roleId,
                CreatedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rolePermissionCodes = await verifyDbContext.AuthRolePermissions
            .Where(link => link.RoleId == roleId)
            .Join(
                verifyDbContext.AuthPermissions,
                link => link.PermissionId,
                permission => permission.Id,
                (_, permission) => permission.Code)
            .ToListAsync();
        var permissionVersion = await verifyDbContext.SystemUsers
            .Where(user => user.Id == userId)
            .Select(user => user.PermissionVersion)
            .SingleAsync();

        rolePermissionCodes.Should().Contain("api:machine-model:read");
        rolePermissionCodes.Should().NotContain("api:machine-model:models");
        permissionVersion.Should().BeGreaterThan(1);

        verifyDbContext.AuthUserOrgUnits.RemoveRange(
            verifyDbContext.AuthUserOrgUnits.Where(link => link.UserId == userId));
        verifyDbContext.AuthUserRoles.RemoveRange(
            verifyDbContext.AuthUserRoles.Where(link => link.UserId == userId));
        verifyDbContext.AuthRolePermissions.RemoveRange(
            verifyDbContext.AuthRolePermissions.Where(link => link.RoleId == roleId));
        verifyDbContext.SystemUsers.RemoveRange(
            verifyDbContext.SystemUsers.Where(user => user.Id == userId));
        verifyDbContext.AuthRoles.RemoveRange(
            verifyDbContext.AuthRoles.Where(role => role.Id == roleId));
        await verifyDbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GetList_AfterSeed_ShouldIncludeMenuPermissions()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        var response = await _client.GetAsync("/api/auth-permissions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(0);

        var permissionItems = body.Data!.EnumerateArray().ToList();
        permissionItems.Should().Contain(item => item.GetProperty("code").GetString() == "menu:config");
        permissionItems.Should().Contain(item => item.GetProperty("code").GetString() == "menu:rbac");
    }

    [Fact]
    public async Task GetList_AfterSeed_ShouldIncludeEmbeddingCacheWarmupManagementPermissions()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        var response = await _client.GetAsync("/api/auth-permissions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(0);

        var permissionCodes = body.Data!.EnumerateArray()
            .Select(item => item.GetProperty("code").GetString())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToList();

        permissionCodes.Should().Contain("page:config:embedding-cache-warmup");
        permissionCodes.Should().Contain("api:embedding-cache-warmup:update");
        permissionCodes.Should().Contain("btn:embedding-cache-warmup:update");
        permissionCodes.Should().Contain("api:embedding-cache-warmup:execute");
        permissionCodes.Should().Contain("btn:embedding-cache-warmup:execute");
        permissionCodes.Should().NotContain("btn:embedding-cache-warmup:create");
    }

    [Fact]
    public async Task GetList_AfterSeed_ShouldHideRemovedLegacyAndExposeOrgUnitCrudPermissions()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        var response = await _client.GetAsync("/api/auth-permissions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(0);

        var permissionCodes = body.Data!.EnumerateArray()
            .Select(item => item.GetProperty("code").GetString())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToList();

        permissionCodes.Should().NotContain("page:config:matching-knowledge");
        permissionCodes.Should().NotContain("btn:matching-knowledge:update");
        permissionCodes.Should().NotContain("btn:matching-knowledge:reset");
        permissionCodes.Should().NotContain("btn:matching-knowledge:generate-draft");
        permissionCodes.Should().NotContain("page:config:text-processing");
        permissionCodes.Should().NotContain("page:other:synonyms");
        permissionCodes.Should().NotContain("page:other:keywords");
        permissionCodes.Should().NotContain("api:auth:routes");
        permissionCodes.Should().Contain("api:org-unit:create");
        permissionCodes.Should().Contain("api:org-unit:delete");
        permissionCodes.Should().Contain("btn:org-unit:create");
        permissionCodes.Should().Contain("btn:org-unit:delete");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var legacyPermissions = await dbContext.AuthPermissions
            .Where(permission =>
                permission.Code == "page:config:text-processing" ||
                permission.Code == "page:other:synonyms" ||
                permission.Code == "page:other:keywords" ||
                permission.Code == "api:auth:routes")
            .ToListAsync();

        legacyPermissions.Should().OnlyContain(permission => !permission.IsActive);
    }

    [Fact]
    public async Task Login_AfterSeed_ShouldIncludeMenuPermissionsInAuthorizationSnapshot()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        using var request = AuthCookieTestHelper.CreateLoginRequest(
            "admin", ApiWebApplicationFactory.TestAdminPassword);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<JsonElement>();
        var permissions = body.GetProperty("data").GetProperty("permissions")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

        permissions.Should().Contain("menu:config");
        permissions.Should().Contain("menu:rbac");
    }

    [Fact]
    public async Task Login_AfterSeed_ShouldHideRemovedPermissionsAndIncludeOrgUnitCrud()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        using var request = AuthCookieTestHelper.CreateLoginRequest(
            "admin", ApiWebApplicationFactory.TestAdminPassword);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<JsonElement>();
        var permissions = body.GetProperty("data").GetProperty("permissions")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

        permissions.Should().NotContain("page:config:matching-knowledge");
        permissions.Should().NotContain("btn:matching-knowledge:update");
        permissions.Should().NotContain("btn:matching-knowledge:reset");
        permissions.Should().NotContain("btn:matching-knowledge:generate-draft");
        permissions.Should().NotContain("page:config:text-processing");
        permissions.Should().NotContain("page:other:synonyms");
        permissions.Should().NotContain("page:other:keywords");
        permissions.Should().NotContain("api:auth:routes");
        permissions.Should().Contain("api:org-unit:create");
        permissions.Should().Contain("api:org-unit:delete");
        permissions.Should().Contain("btn:org-unit:create");
        permissions.Should().Contain("btn:org-unit:delete");
    }

    [Fact]
    public async Task Login_CommonUserAfterSeed_ShouldIncludeMainWorkflowButtonPermissions()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        using var request = AuthCookieTestHelper.CreateLoginRequest(
            "common", ApiWebApplicationFactory.TestCommonPassword);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<JsonElement>();
        var permissions = body.GetProperty("data").GetProperty("permissions")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

        permissions.Should().Contain("btn:document:upload");
        permissions.Should().Contain("btn:document:import");
        permissions.Should().Contain("btn:excel-document:import");
        permissions.Should().Contain("api:document:preview");
        permissions.Should().Contain("api:smart-config:create");
        permissions.Should().Contain("btn:smart-config:create");
        permissions.Should().Contain("api:machine-model:read");
        permissions.Should().Contain("api:ai-service:read");
        permissions.Should().Contain("btn:file-compare:upload");
        permissions.Should().Contain("btn:file-compare:preview");
        permissions.Should().Contain("btn:file-compare:download");
        permissions.Should().Contain("btn:matching:preview-batch");
        permissions.Should().Contain("btn:matching:download");
        permissions.Should().Contain("btn:matching-fill:llm-stream");
        permissions.Should().Contain("btn:matching-fill:execute-batch");
        permissions.Should().Contain("api:matching-fill:spec-backfill");
        permissions.Should().Contain("api:dashboard:read");
        permissions.Should().Contain("api:matching:read");
        permissions.Should().Contain("menu:base-data");
        permissions.Should().Contain("page:base-data:customers");
        permissions.Should().Contain("page:base-data:processes");
        permissions.Should().Contain("page:base-data:machine-models");
        permissions.Should().Contain("page:base-data:specs");
        permissions.Should().Contain("btn:customer:create");
        permissions.Should().Contain("btn:customer:update");
        permissions.Should().Contain("btn:customer:delete");
        permissions.Should().Contain("btn:process:create");
        permissions.Should().Contain("btn:process:update");
        permissions.Should().Contain("btn:process:delete");
        permissions.Should().Contain("btn:machine-model:create");
        permissions.Should().Contain("btn:machine-model:update");
        permissions.Should().Contain("btn:machine-model:delete");
        permissions.Should().Contain("btn:spec:create");
        permissions.Should().Contain("btn:spec:update");
        permissions.Should().Contain("btn:spec:delete");
        permissions.Should().Contain("btn:spec:remark-replace");
        permissions.Should().Contain("menu:rbac");
        permissions.Should().Contain("page:config:system-users");
        permissions.Should().Contain("api:system-user:read");
        permissions.Should().Contain("btn:system-user:create");
        permissions.Should().Contain("btn:system-user:update");
        permissions.Should().Contain("btn:system-user:update-status");
        permissions.Should().Contain("btn:system-user:reset-password");
        permissions.Should().Contain("btn:system-user:delete");
        permissions.Should().Contain("api:auth-role:read");
        permissions.Should().Contain("api:org-unit:read");
        permissions.Should().NotContain("api:machine-model:models");
        permissions.Should().NotContain("btn:machine-model:models");
        permissions.Should().NotContain("btn:matching:preview");
        permissions.Should().NotContain("btn:matching-fill:execute");
        permissions.Should().NotContain("btn:matching:llm-stream");
        permissions.Should().NotContain("api:matching:execute");
        permissions.Should().NotContain("api:matching:execute-batch");
        permissions.Should().NotContain("api:matching:llm-stream");
    }

    [Fact]
    public async Task Login_CommonUserAfterSeed_ShouldIncludeBatchReplyPermissions()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        using var request = AuthCookieTestHelper.CreateLoginRequest(
            "common", ApiWebApplicationFactory.TestCommonPassword);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<JsonElement>();
        var permissions = body.GetProperty("data").GetProperty("permissions")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

        permissions.Should().Contain("menu:batch-reply");
        permissions.Should().Contain("page:batch-reply:index");
        permissions.Should().Contain("api:batch-reply:upload");
        permissions.Should().Contain("api:batch-reply:upload-source");
        permissions.Should().Contain("api:batch-reply:preview");
        permissions.Should().Contain("api:batch-reply:execute");
        permissions.Should().Contain("api:batch-reply:download");
        permissions.Should().Contain("btn:batch-reply:preview");
        permissions.Should().Contain("btn:batch-reply:execute");
    }

    [Fact]
    public async Task Login_CommonUserAfterSeed_ShouldNotIncludeLegacyMatchingSimilarityPermission()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        using var request = AuthCookieTestHelper.CreateLoginRequest(
            "common", ApiWebApplicationFactory.TestCommonPassword);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<JsonElement>();
        var permissions = body.GetProperty("data").GetProperty("permissions")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

        permissions.Should().NotContain("api:matching:similarity");
        permissions.Should().NotContain("btn:matching:similarity");
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "web")) &&
                Directory.Exists(Path.Combine(current.FullName, "shared")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("未找到仓库根目录");
    }
}
