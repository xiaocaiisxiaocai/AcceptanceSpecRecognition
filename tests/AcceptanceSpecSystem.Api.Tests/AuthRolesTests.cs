using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task Update_WhenRoleBuiltIn_ShouldReturnBadRequest()
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
    }

    [Fact]
    public async Task Create_WhenDataScopeUsesCustomMultipleOrgNodes_ShouldReturnBadRequest()
    {
        var roleCode = $"multi-{Guid.NewGuid():N}"[..18];
        var rootOrgUnitId = await GetRootOrgUnitIdAsync();

        var response = await _client.PostAsync(
            "/api/auth-roles",
            ApiClientJson.ToJsonContent(new
            {
                code = roleCode,
                name = "非法多组织角色",
                description = "测试单组织限制",
                isActive = true,
                permissionCodes = Array.Empty<string>(),
                dataScopes = new[]
                {
                    new
                    {
                        resource = "spec",
                        scopeType = 3,
                        orgUnitIds = new[] { rootOrgUnitId }
                    }
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("单组织");
    }

    [Fact]
    public async Task Create_WhenDataScopeTargetsNonRootOrgNode_ShouldReturnBadRequest()
    {
        var roleCode = $"node-{Guid.NewGuid():N}"[..18];
        var childOrgUnitId = await SeedChildOrgUnitAsync();

        var response = await _client.PostAsync(
            "/api/auth-roles",
            ApiClientJson.ToJsonContent(new
            {
                code = roleCode,
                name = "非法组织范围角色",
                description = "测试根组织约束",
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

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("根组织");
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
