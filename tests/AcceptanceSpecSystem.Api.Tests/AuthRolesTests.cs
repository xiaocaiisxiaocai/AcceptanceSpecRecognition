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
                name = "管理员-修改",
                description = "不允许修改",
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

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("内置角色不允许编辑");
    }

    [Fact]
    public async Task Update_WhenCustomRoleChanged_ShouldBumpAssignedUserPermissionVersion()
    {
        int roleId;
        int originalPermissionVersion;

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var commonUser = await dbContext.SystemUsers.FirstAsync(user => user.Username == "common");
            originalPermissionVersion = commonUser.PermissionVersion;

            var role = new AuthRole
            {
                CompanyId = commonUser.CompanyId,
                Code = $"review-{Guid.NewGuid():N}"[..18],
                Name = "协作角色",
                Description = "初始角色",
                IsBuiltIn = false,
                IsActive = true,
                CreatedAt = DateTime.Now
            };
            await dbContext.AuthRoles.AddAsync(role);
            await dbContext.SaveChangesAsync();

            await dbContext.AuthUserRoles.AddAsync(new AuthUserRole
            {
                UserId = commonUser.Id,
                RoleId = role.Id,
                CreatedAt = DateTime.Now
            });
            await dbContext.SaveChangesAsync();
            roleId = role.Id;
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
        var commonUserAfterUpdate = await verifyDbContext.SystemUsers.FirstAsync(user => user.Username == "common");
        commonUserAfterUpdate.PermissionVersion.Should().Be(originalPermissionVersion + 1);
    }
}
