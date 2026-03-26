using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class SystemUsersTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly ApiWebApplicationFactory _factory;

    public SystemUsersTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetList_ShouldContainSeedUsers()
    {
        var resp = await _client.GetAsync("/api/system-users?page=1&pageSize=20");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.ReadAsAsync<ApiResponse<PagedData<JsonElement>>>();
        body.Code.Should().Be(0);
        body.Data.Should().NotBeNull();

        var usernames = body.Data!.Items
            .Select(x => x.GetProperty("username").GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        usernames.Should().Contain("admin");
        usernames.Should().Contain("common");
    }

    [Fact]
    public async Task Create_And_ResetPassword_ShouldLoginWithNewPassword()
    {
        var rootOrgUnitId = await GetRootOrgUnitIdAsync();
        var createResp = await _client.PostAsync(
            "/api/system-users",
            ApiClientJson.ToJsonContent(new
            {
                username = "test_user_01",
                password = "User@123456",
                nickname = "测试用户",
                avatar = "",
                roleCode = "common",
                orgUnitId = rootOrgUnitId,
                isActive = true
            }));
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await createResp.ReadAsAsync<ApiResponse<JsonElement>>();
        created.Code.Should().Be(0);
        created.Data!.GetProperty("roleCode").GetString().Should().Be("common");
        var userId = created.Data!.GetProperty("id").GetInt32();

        var resetResp = await _client.PutAsync(
            $"/api/system-users/{userId}/password",
            ApiClientJson.ToJsonContent(new { newPassword = "User@654321" }));
        resetResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var oldLoginResp = await _client.PostAsync(
            "/login",
            ApiClientJson.ToJsonContent(new { username = "test_user_01", password = "User@123456" }));
        oldLoginResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var newLoginResp = await _client.PostAsync(
            "/login",
            ApiClientJson.ToJsonContent(new { username = "test_user_01", password = "User@654321" }));
        newLoginResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_WithLegacyRolesArray_ShouldReturnBadRequest()
    {
        var rootOrgUnitId = await GetRootOrgUnitIdAsync();
        var createResp = await _client.PostAsync(
            "/api/system-users",
            ApiClientJson.ToJsonContent(new
            {
                username = "test_user_legacy",
                password = "User@123456",
                nickname = "旧格式用户",
                avatar = "",
                roles = new[] { "common" },
                orgUnitId = rootOrgUnitId,
                isActive = true
            }));

        createResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithSingleOrgField_ShouldReturnSingleOrgFields()
    {
        var rootOrgUnitId = await GetRootOrgUnitIdAsync();
        var username = $"single_org_{Guid.NewGuid():N}"[..18];

        var createResp = await _client.PostAsync(
            "/api/system-users",
            ApiClientJson.ToJsonContent(new
            {
                username,
                password = "User@123456",
                nickname = "单组织用户",
                avatar = "",
                roleCode = "common",
                orgUnitId = rootOrgUnitId,
                isActive = true
            }));

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await createResp.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(0);
        body.Data!.GetProperty("orgUnitId").GetInt32().Should().Be(rootOrgUnitId);
        body.Data!.GetProperty("orgUnitName").GetString().Should().NotBeNullOrWhiteSpace();
        body.Data!.TryGetProperty("orgUnits", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Create_WithLegacyOrgFields_ShouldReturnBadRequest()
    {
        var rootOrgUnitId = await GetRootOrgUnitIdAsync();
        var username = $"legacy_org_{Guid.NewGuid():N}"[..18];

        var createResp = await _client.PostAsync(
            "/api/system-users",
            ApiClientJson.ToJsonContent(new
            {
                username,
                password = "User@123456",
                nickname = "旧组织口径用户",
                avatar = "",
                roleCode = "common",
                primaryOrgUnitId = rootOrgUnitId,
                orgUnitIds = new[] { rootOrgUnitId },
                isActive = true
            }));

        createResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_WithLegacyOrgFields_ShouldReturnBadRequest()
    {
        var rootOrgUnitId = await GetRootOrgUnitIdAsync();
        var username = $"legacy_upd_{Guid.NewGuid():N}"[..18];

        var createResp = await _client.PostAsync(
            "/api/system-users",
            ApiClientJson.ToJsonContent(new
            {
                username,
                password = "User@123456",
                nickname = "待更新用户",
                avatar = "",
                roleCode = "common",
                orgUnitId = rootOrgUnitId,
                isActive = true
            }));
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await createResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var userId = created.Data!.GetProperty("id").GetInt32();

        var updateResp = await _client.PutAsync(
            $"/api/system-users/{userId}",
            ApiClientJson.ToJsonContent(new
            {
                nickname = "更新后用户",
                avatar = "",
                roleCode = "common",
                primaryOrgUnitId = rootOrgUnitId,
                orgUnitIds = new[] { rootOrgUnitId },
                isActive = true
            }));

        updateResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithNonRootOrgUnit_ShouldReturnBadRequest()
    {
        var childOrgUnitId = await SeedChildOrgUnitAsync();
        var username = $"child_org_{Guid.NewGuid():N}"[..18];

        var createResp = await _client.PostAsync(
            "/api/system-users",
            ApiClientJson.ToJsonContent(new
            {
                username,
                password = "User@123456",
                nickname = "非法组织用户",
                avatar = "",
                roleCode = "common",
                orgUnitId = childOrgUnitId,
                isActive = true
            }));

        createResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await createResp.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("根组织");
    }

    [Fact]
    public async Task Delete_LastActiveAdmin_ShouldFail()
    {
        var listResp = await _client.GetAsync("/api/system-users?page=1&pageSize=20");
        var list = await listResp.ReadAsAsync<ApiResponse<PagedData<JsonElement>>>();
        list.Code.Should().Be(0);

        var admin = list.Data!.Items.First(x => x.GetProperty("username").GetString() == "admin");
        var adminId = admin.GetProperty("id").GetInt32();

        var deleteResp = await _client.DeleteAsync($"/api/system-users/{adminId}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await deleteResp.ReadAsAsync<ApiResponse<object>>();
        body.Code.Should().Be(400);
        body.Message.Should().Contain("至少需要保留一个启用状态的 admin 用户");
    }

    [Fact]
    public async Task GetList_WhenRoleCommon_ShouldReturnForbidden()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/system-users?page=1&pageSize=20");
        req.Headers.Add("X-Test-Role", "common");

        using var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetList_WhenAnonymous_ShouldReturnUnauthorized()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/system-users?page=1&pageSize=20");
        req.Headers.Add("X-Test-Auth", "anonymous");

        using var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<int> GetRootOrgUnitIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.OrgUnits
            .AsNoTracking()
            .Where(org => org.UnitType == OrgUnitType.Company && org.ParentId == null)
            .OrderBy(org => org.Id)
            .Select(org => org.Id)
            .FirstAsync();
    }

    private async Task<int> SeedChildOrgUnitAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rootOrgUnitId = await dbContext.OrgUnits
            .AsNoTracking()
            .Where(org => org.UnitType == OrgUnitType.Company && org.ParentId == null)
            .OrderBy(org => org.Id)
            .Select(org => org.Id)
            .FirstAsync();

        var child = new OrgUnit
        {
            CompanyId = 1,
            ParentId = rootOrgUnitId,
            UnitType = OrgUnitType.Division,
            Code = $"DIV-{Guid.NewGuid():N}"[..18],
            Name = "用户测试事业部",
            Path = $"/{rootOrgUnitId}/",
            Depth = 1,
            Sort = 0,
            IsActive = true,
            CreatedAt = DateTime.Now
        };

        dbContext.OrgUnits.Add(child);
        await dbContext.SaveChangesAsync();
        child.Path = $"/{rootOrgUnitId}/{child.Id}/";
        await dbContext.SaveChangesAsync();
        return child.Id;
    }
}
