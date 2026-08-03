using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Api.Tests;

public class AuthRolesTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly ApiWebApplicationFactory _factory;

    public AuthRolesTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Create_WhenDataScopeContainsInvalidOrgUnit_ShouldNotPersistRole()
    {
        var roleCode = $"role-{Guid.NewGuid():N}"[..18];

        var response = await _client.PostAsync(
            "/api/auth-roles",
            ApiClientJson.ToJsonContent(new
            {
                code = roleCode,
                name = "测试角色",
                description = "测试",
                isActive = true,
                permissionCodes = Array.Empty<string>(),
                dataScopes = new[]
                {
                    new
                    {
                        resource = "spec",
                        scopeType = 3,
                        orgUnitIds = new[] { 999999 }
                    }
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exists = await dbContext.AuthRoles.AnyAsync(role => role.Code == roleCode);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task Create_WhenCustomRoleUsesEmptyOrgSubtree_ShouldReturnBadRequest()
    {
        var roleCode = $"scope-{Guid.NewGuid():N}"[..18];

        var response = await _client.PostAsync(
            "/api/auth-roles",
            ApiClientJson.ToJsonContent(new
            {
                code = roleCode,
                name = "空子树测试角色",
                description = "自定义角色不能使用动态主组织范围",
                isActive = true,
                permissionCodes = Array.Empty<string>(),
                dataScopes = new[]
                {
                    new
                    {
                        resource = "spec",
                        scopeType = 2,
                        orgUnitIds = Array.Empty<int>()
                    }
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("必须选择一个组织节点");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await dbContext.AuthRoles.AnyAsync(role => role.Code == roleCode)).Should().BeFalse();
    }

    [Fact]
    public async Task Update_WhenAdminRoleBuiltIn_ShouldReturnBadRequest()
    {
        var updatedName = $"管理员-{Guid.NewGuid():N}"[..12];

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminRoleId = await dbContext.AuthRoles
            .Where(role => role.Code == "admin")
            .Select(role => role.Id)
            .FirstAsync();

        var response = await _client.PutAsync(
            $"/api/auth-roles/{adminRoleId}",
            ApiClientJson.ToJsonContent(new
            {
                name = updatedName,
                description = "允许修改内置角色",
                isActive = false,
                permissionCodes = Array.Empty<string>(),
                dataScopes = new[]
                {
                    new
                    {
                        resource = "spec",
                        scopeType = 4,
                        orgUnitIds = Array.Empty<int>()
                    }
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(400);
        body.Message.Should().Contain("内置角色不允许修改");

        var updatedRole = await dbContext.AuthRoles.FirstAsync(role => role.Id == adminRoleId);
        updatedRole.Name.Should().NotBe(updatedName);
        updatedRole.Description.Should().NotBe("允许修改内置角色");
        updatedRole.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Update_WhenCommonRoleBuiltIn_ShouldPersistAdministratorChangesAcrossSeedRuns()
    {
        using var isolatedFactory = new ApiWebApplicationFactory();
        using var client = isolatedFactory.CreateClient();
        using var scope = isolatedFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var commonRoleId = await dbContext.AuthRoles
            .Where(role => role.Code == "common")
            .Select(role => role.Id)
            .SingleAsync();

        var originalResponse = await client.GetAsync($"/api/auth-roles/{commonRoleId}");
        originalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var originalBody = await originalResponse.ReadAsAsync<ApiResponse<AuthRoleDto>>();
        var originalRole = originalBody.Data!;
        var customizedDescription = $"管理员自定义-{Guid.NewGuid():N}";
        var customizedPermissionCodes = originalRole.PermissionCodes.Take(1).ToArray();

        try
        {
            var updateResponse = await client.PutAsync(
                $"/api/auth-roles/{commonRoleId}",
                ApiClientJson.ToJsonContent(new
                {
                    name = originalRole.Name,
                    description = customizedDescription,
                    isActive = true,
                    permissionCodes = customizedPermissionCodes,
                    dataScopes = new[]
                    {
                        new
                        {
                            resource = "spec",
                            scopeType = 0,
                            orgUnitIds = Array.Empty<int>()
                        }
                    }
                }));

            updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            await AuthUserSeedService.EnsureSeedUsersAsync(isolatedFactory.Services, NullLogger.Instance);

            var persistedResponse = await client.GetAsync($"/api/auth-roles/{commonRoleId}");
            persistedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var persistedBody = await persistedResponse.ReadAsAsync<ApiResponse<AuthRoleDto>>();
            persistedBody.Data!.Description.Should().Be(customizedDescription);
            persistedBody.Data.PermissionCodes.Should().BeEquivalentTo(customizedPermissionCodes);
            persistedBody.Data.DataScopes.Should().ContainSingle(scopeDto =>
                scopeDto.Resource == "spec" &&
                scopeDto.ScopeType == DataScopeType.Self &&
                scopeDto.OrgUnitIds.Count == 0);
        }
        finally
        {
            var restoreResponse = await client.PutAsync(
                $"/api/auth-roles/{commonRoleId}",
                ApiClientJson.ToJsonContent(new
                {
                    name = originalRole.Name,
                    description = originalRole.Description,
                    isActive = originalRole.IsActive,
                    permissionCodes = originalRole.PermissionCodes,
                    dataScopes = originalRole.DataScopes
                }));
            restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Update_WhenCommonRoleUsesDynamicPrimaryOrgSubtree_ShouldPreserveEmptyNodes()
    {
        using var isolatedFactory = new ApiWebApplicationFactory();
        using var client = isolatedFactory.CreateClient();
        using var scope = isolatedFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var commonRoleId = await dbContext.AuthRoles
            .Where(role => role.Code == "common")
            .Select(role => role.Id)
            .SingleAsync();

        var originalResponse = await client.GetAsync($"/api/auth-roles/{commonRoleId}");
        originalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var originalBody = await originalResponse.ReadAsAsync<ApiResponse<AuthRoleDto>>();
        var originalRole = originalBody.Data!;
        originalRole.DataScopes.Should().ContainSingle(scopeDto =>
            scopeDto.Resource == "spec" &&
            scopeDto.ScopeType == DataScopeType.OrgSubtree &&
            scopeDto.OrgUnitIds.Count == 0);

        var updateResponse = await client.PutAsync(
            $"/api/auth-roles/{commonRoleId}",
            ApiClientJson.ToJsonContent(new
            {
                name = originalRole.Name,
                description = originalRole.Description,
                isActive = originalRole.IsActive,
                permissionCodes = originalRole.PermissionCodes,
                dataScopes = originalRole.DataScopes
            }));

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var persistedResponse = await client.GetAsync($"/api/auth-roles/{commonRoleId}");
        persistedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var persistedBody = await persistedResponse.ReadAsAsync<ApiResponse<AuthRoleDto>>();
        persistedBody.Data!.DataScopes.Should().ContainSingle(scopeDto =>
            scopeDto.Resource == "spec" &&
            scopeDto.ScopeType == DataScopeType.OrgSubtree &&
            scopeDto.OrgUnitIds.Count == 0);
    }

    [Theory]
    [InlineData("spec", 1)]
    [InlineData("other", 2)]
    public async Task Update_WhenCommonRoleUsesUnsupportedEmptyScope_ShouldReturnBadRequest(
        string resource,
        int scopeType)
    {
        using var isolatedFactory = new ApiWebApplicationFactory();
        using var client = isolatedFactory.CreateClient();
        using var scope = isolatedFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var commonRoleId = await dbContext.AuthRoles
            .Where(role => role.Code == "common")
            .Select(role => role.Id)
            .SingleAsync();

        var originalResponse = await client.GetAsync($"/api/auth-roles/{commonRoleId}");
        originalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var originalBody = await originalResponse.ReadAsAsync<ApiResponse<AuthRoleDto>>();
        var originalRole = originalBody.Data!;

        var updateResponse = await client.PutAsync(
            $"/api/auth-roles/{commonRoleId}",
            ApiClientJson.ToJsonContent(new
            {
                name = originalRole.Name,
                description = originalRole.Description,
                isActive = originalRole.IsActive,
                permissionCodes = originalRole.PermissionCodes,
                dataScopes = new[]
                {
                    new
                    {
                        resource,
                        scopeType,
                        orgUnitIds = Array.Empty<int>()
                    }
                }
            }));

        updateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await updateResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("必须选择一个组织节点");
    }

    [Fact]
    public async Task Delete_WhenRoleBuiltIn_ShouldReturnBadRequest()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminRoleId = await dbContext.AuthRoles
            .Where(role => role.Code == "admin")
            .Select(role => role.Id)
            .FirstAsync();

        var response = await _client.DeleteAsync($"/api/auth-roles/{adminRoleId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("内置角色不允许删除");
    }

    [Fact]
    public async Task Update_WhenCustomRoleChanged_ShouldBumpAssignedUserPermissionVersion()
    {
        int roleId;
        int testUserId;
        int originalPermissionVersion;

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var commonUser = await dbContext.SystemUsers.FirstAsync(user => user.Username == "common");

            var role = new AuthRole
            {
                CompanyId = commonUser.CompanyId,
                Code = $"review-{Guid.NewGuid():N}"[..18],
                Name = "协作角色",
                Description = "初始角色",
                IsBuiltIn = false,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await dbContext.AuthRoles.AddAsync(role);
            await dbContext.SaveChangesAsync();

            var assignedUser = new SystemUser
            {
                CompanyId = commonUser.CompanyId,
                Username = $"role_user_{Guid.NewGuid():N}"[..18],
                PasswordHash = "test-hash",
                Nickname = "角色测试用户",
                Avatar = string.Empty,
                IsActive = true,
                PermissionVersion = 1,
                CreatedAt = DateTime.UtcNow
            };
            await dbContext.SystemUsers.AddAsync(assignedUser);
            await dbContext.SaveChangesAsync();

            await dbContext.AuthUserRoles.AddAsync(new AuthUserRole
            {
                UserId = assignedUser.Id,
                RoleId = role.Id,
                CreatedAt = DateTime.UtcNow
            });
            await dbContext.AuthRefreshSessions.AddAsync(new AuthRefreshSession
            {
                FamilyId = Guid.NewGuid().ToString("N"),
                UserId = assignedUser.Id,
                PermissionVersion = assignedUser.PermissionVersion,
                TokenHash = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTime.UtcNow.AddDays(1)
            });
            await dbContext.SaveChangesAsync();
            roleId = role.Id;
            testUserId = assignedUser.Id;
            originalPermissionVersion = assignedUser.PermissionVersion;
        }

        var response = await _client.PutAsync(
            $"/api/auth-roles/{roleId}",
            ApiClientJson.ToJsonContent(new
            {
                name = "协作角色-更新",
                description = "更新后",
                isActive = true,
                permissionCodes = Array.Empty<string>(),
                dataScopes = new[]
                {
                    new
                    {
                        resource = "spec",
                        scopeType = 4,
                        orgUnitIds = Array.Empty<int>()
                    }
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var testUserAfterUpdate = await verifyDbContext.SystemUsers.FirstAsync(user => user.Id == testUserId);
        testUserAfterUpdate.PermissionVersion.Should().Be(originalPermissionVersion + 1);
        var refreshSession = await verifyDbContext.AuthRefreshSessions
            .SingleAsync(session => session.UserId == testUserId);
        refreshSession.Status.Should().Be(AuthRefreshSessionStatus.Revoked);
    }

    [Fact]
    public async Task Create_WhenDataScopeUsesCustomMultipleOrgNodes_ShouldPersistEveryNode()
    {
        var roleCode = $"multi-{Guid.NewGuid():N}"[..18];
        var rootOrgUnitId = await GetRootOrgUnitIdAsync();
        var childOrgUnitId = await SeedChildOrgUnitAsync();

        var response = await _client.PostAsync(
            "/api/auth-roles",
            ApiClientJson.ToJsonContent(new
            {
                code = roleCode,
                name = "多组织范围角色",
                description = "测试自定义组织范围",
                isActive = true,
                permissionCodes = Array.Empty<string>(),
                dataScopes = new[]
                {
                    new
                    {
                        resource = "spec",
                        scopeType = 3,
                        orgUnitIds = new[] { rootOrgUnitId, childOrgUnitId }
                    }
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var roleId = body.Data.GetProperty("id").GetInt32();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var savedNodeIds = await dbContext.AuthRoleDataScopeNodes
            .Where(node => node.RoleDataScope.RoleId == roleId)
            .Select(node => node.OrgUnitId)
            .OrderBy(id => id)
            .ToListAsync();
        savedNodeIds.Should().Equal(new[] { rootOrgUnitId, childOrgUnitId }.OrderBy(id => id));
    }

    [Fact]
    public async Task Create_WhenDataScopeTargetsActiveNonRootOrgNode_ShouldSucceed()
    {
        var roleCode = $"node-{Guid.NewGuid():N}"[..18];
        var childOrgUnitId = await SeedChildOrgUnitAsync();

        var response = await _client.PostAsync(
            "/api/auth-roles",
            ApiClientJson.ToJsonContent(new
            {
                code = roleCode,
                name = "事业部范围角色",
                description = "测试子节点范围",
                isActive = true,
                permissionCodes = Array.Empty<string>(),
                dataScopes = new[]
                {
                    new
                    {
                        resource = "spec",
                        scopeType = 1,
                        orgUnitIds = new[] { childOrgUnitId }
                    }
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Data.GetProperty("dataScopes")[0]
            .GetProperty("orgUnitIds")
            .EnumerateArray()
            .Select(item => item.GetInt32())
            .Should()
            .Equal(childOrgUnitId);
    }

    private async Task<int> GetRootOrgUnitIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.OrgUnits
            .Where(org => org.UnitType == OrgUnitType.Company && org.ParentId == null)
            .Select(org => org.Id)
            .FirstAsync();
    }

    private async Task<int> SeedChildOrgUnitAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rootOrgUnitId = await GetRootOrgUnitIdAsync();

        var child = new OrgUnit
        {
            CompanyId = 1,
            ParentId = rootOrgUnitId,
            UnitType = OrgUnitType.Division,
            Code = $"DIV-{Guid.NewGuid():N}"[..18],
            Name = "角色测试事业部",
            Path = $"/{rootOrgUnitId}/",
            Depth = 1,
            Sort = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.OrgUnits.Add(child);
        await dbContext.SaveChangesAsync();
        child.Path = $"/{rootOrgUnitId}/{child.Id}/";
        await dbContext.SaveChangesAsync();
        return child.Id;
    }
}
