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
                username = "test_u01",
                password = "1234",
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

        using var initialLoginRequest = AuthCookieTestHelper.CreateLoginRequest(
            "test_u01", "1234");
        using var initialLoginResp = await _client.SendAsync(initialLoginRequest);
        initialLoginResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var resetResp = await _client.PutAsync(
            $"/api/system-users/{userId}/password",
            ApiClientJson.ToJsonContent(new { newPassword = "5678" }));
        resetResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sessions = await dbContext.AuthRefreshSessions
                .Where(session => session.UserId == userId)
                .ToListAsync();
            sessions.Should().NotBeEmpty();
            sessions.Should().OnlyContain(session => session.Status == AuthRefreshSessionStatus.Revoked);
        }

        using var oldLoginRequest = AuthCookieTestHelper.CreateLoginRequest(
            "test_u01", "1234");
        var oldLoginResp = await _client.SendAsync(oldLoginRequest);
        oldLoginResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var newLoginRequest = AuthCookieTestHelper.CreateLoginRequest(
            "test_u01", "5678");
        var newLoginResp = await _client.SendAsync(newLoginRequest);
        newLoginResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_WithChineseUsername_ShouldCreateAndLogin()
    {
        var rootOrgUnitId = await GetRootOrgUnitIdAsync();
        const string username = "张三";

        var createResp = await _client.PostAsync(
            "/api/system-users",
            ApiClientJson.ToJsonContent(new
            {
                username,
                password = "User@1234567",
                nickname = username,
                avatar = "",
                roleCode = "common",
                orgUnitId = rootOrgUnitId,
                isActive = true
            }));

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.ReadAsAsync<ApiResponse<JsonElement>>();
        created.Data!.GetProperty("username").GetString().Should().Be(username);

        using var loginRequest = AuthCookieTestHelper.CreateLoginRequest(
            username, "User@1234567");
        using var loginResp = await _client.SendAsync(loginRequest);
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_WithUsernameLongerThanTenCharacters_ShouldReturnBadRequest()
    {
        var rootOrgUnitId = await GetRootOrgUnitIdAsync();
        var createResp = await _client.PostAsync(
            "/api/system-users",
            ApiClientJson.ToJsonContent(new
            {
                username = "abcdefghijk",
                password = "User@1234567",
                nickname = "超长用户名",
                avatar = "",
                roleCode = "common",
                orgUnitId = rootOrgUnitId,
                isActive = true
            }));

        createResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithLegacyRolesArray_ShouldReturnBadRequest()
    {
        var rootOrgUnitId = await GetRootOrgUnitIdAsync();
        var createResp = await _client.PostAsync(
            "/api/system-users",
            ApiClientJson.ToJsonContent(new
            {
                username = "legacy01",
                password = "User@1234567",
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
        var username = CreateUniqueUsername("single");

        var createResp = await _client.PostAsync(
            "/api/system-users",
            ApiClientJson.ToJsonContent(new
            {
                username,
                password = "User@1234567",
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
        var username = CreateUniqueUsername("legacy");

        var createResp = await _client.PostAsync(
            "/api/system-users",
            ApiClientJson.ToJsonContent(new
            {
                username,
                password = "User@1234567",
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
        var username = CreateUniqueUsername("update");

        var createResp = await _client.PostAsync(
            "/api/system-users",
            ApiClientJson.ToJsonContent(new
            {
                username,
                password = "User@1234567",
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
    public async Task Create_WithActiveNonRootOrgUnit_ShouldAssignExactlyThatNode()
    {
        var childOrgUnitId = await SeedChildOrgUnitAsync();
        var username = CreateUniqueUsername("child");

        var createResp = await _client.PostAsync(
            "/api/system-users",
            ApiClientJson.ToJsonContent(new
            {
                username,
                password = "User@1234567",
                nickname = "事业部用户",
                avatar = "",
                roleCode = "common",
                orgUnitId = childOrgUnitId,
                isActive = true
            }));

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await createResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var userId = body.Data.GetProperty("id").GetInt32();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assignments = await dbContext.AuthUserOrgUnits
            .Where(link => link.UserId == userId)
            .Select(link => link.OrgUnitId)
            .ToListAsync();
        assignments.Should().Equal(childOrgUnitId);
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
    public async Task UpdateAdmin_WhenOnlyReplacementIsFuture_ShouldFail()
    {
        var rootOrgUnitId = await GetRootOrgUnitIdAsync();
        await CreateAdminAsync(
            CreateUniqueUsername("future"),
            rootOrgUnitId,
            DateTime.UtcNow.AddHours(2),
            null);

        var adminId = await GetSeedAdminIdAsync();
        var response = await UpdateAdminAsync(
            adminId,
            rootOrgUnitId,
            roleCode: "common",
            roleStartAt: null,
            roleEndAt: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadAsAsync<ApiResponse<object>>()).Message
            .Should().Contain("至少需要保留一个启用状态的 admin 用户");
    }

    [Fact]
    public async Task UpdateAdmin_WhenOnlyReplacementIsExpired_ShouldFail()
    {
        var rootOrgUnitId = await GetRootOrgUnitIdAsync();
        await CreateAdminAsync(
            CreateUniqueUsername("expired"),
            rootOrgUnitId,
            DateTime.UtcNow.AddHours(-2),
            DateTime.UtcNow.AddHours(-1));

        var adminId = await GetSeedAdminIdAsync();
        var response = await UpdateAdminAsync(
            adminId,
            rootOrgUnitId,
            roleCode: "common",
            roleStartAt: null,
            roleEndAt: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateAdmin_WhenScheduledReplacementLeavesGap_ShouldFail()
    {
        var rootOrgUnitId = await GetRootOrgUnitIdAsync();
        var now = DateTime.UtcNow;
        await CreateAdminAsync(
            CreateUniqueUsername("gap"),
            rootOrgUnitId,
            now.AddHours(2),
            null);

        var adminId = await GetSeedAdminIdAsync();
        var response = await UpdateAdminAsync(
            adminId,
            rootOrgUnitId,
            roleCode: "admin",
            roleStartAt: null,
            roleEndAt: now.AddHours(1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteAdmins_Concurrently_ShouldPreserveOneEffectiveAdmin()
    {
        await using var isolatedFactory = new ApiWebApplicationFactory();
        var client = isolatedFactory.CreateClient();
        var (firstAdminId, secondAdminId) = await SeedConcurrentAdminScenarioAsync(isolatedFactory);

        var responses = await Task.WhenAll(
            client.DeleteAsync($"/api/system-users/{firstAdminId}"),
            client.DeleteAsync($"/api/system-users/{secondAdminId}"));

        responses.Count(response => response.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest).Should().Be(1);

        using var scope = isolatedFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var effectiveAdminCount = await dbContext.SystemUsers
            .AsNoTracking()
            .Where(user => user.IsActive)
            .SelectMany(user => user.UserRoles)
            .CountAsync(link =>
                link.Role.Code == "admin" &&
                link.Role.IsActive &&
                (!link.StartAt.HasValue || link.StartAt <= now) &&
                (!link.EndAt.HasValue || link.EndAt >= now));
        effectiveAdminCount.Should().Be(1);
    }

    [Fact]
    public async Task Delete_FutureAdminRequiredForContinuousCoverage_ShouldFail()
    {
        await using var isolatedFactory = new ApiWebApplicationFactory();
        var client = isolatedFactory.CreateClient();
        var futureAdminId = await SeedFutureCoverageScenarioAsync(isolatedFactory);

        using var response = await client.DeleteAsync($"/api/system-users/{futureAdminId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadAsAsync<ApiResponse<object>>()).Message
            .Should().Contain("覆盖区间必须连续");
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

    private static string CreateUniqueUsername(string prefix)
    {
        var suffixLength = 10 - prefix.Length;
        return $"{prefix}{Guid.NewGuid():N}"[..(prefix.Length + suffixLength)];
    }

    private async Task<int> GetSeedAdminIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.SystemUsers
            .AsNoTracking()
            .Where(user => user.Username == "admin")
            .Select(user => user.Id)
            .SingleAsync();
    }

    private async Task CreateAdminAsync(
        string username,
        int rootOrgUnitId,
        DateTime? roleStartAt,
        DateTime? roleEndAt)
    {
        var response = await _client.PostAsync(
            "/api/system-users",
            ApiClientJson.ToJsonContent(new
            {
                username,
                password = "Admin@1234567",
                nickname = username,
                avatar = "",
                roleCode = "admin",
                orgUnitId = rootOrgUnitId,
                roleStartAt,
                roleEndAt,
                isActive = true
            }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> UpdateAdminAsync(
        int adminId,
        int rootOrgUnitId,
        string roleCode,
        DateTime? roleStartAt,
        DateTime? roleEndAt)
    {
        return await _client.PutAsync(
            $"/api/system-users/{adminId}",
            ApiClientJson.ToJsonContent(new
            {
                nickname = "管理员",
                avatar = "",
                roleCode,
                orgUnitId = rootOrgUnitId,
                roleStartAt,
                roleEndAt,
                isActive = true
            }));
    }

    private static async Task<(int FirstAdminId, int SecondAdminId)> SeedConcurrentAdminScenarioAsync(
        ApiWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminRole = await dbContext.AuthRoles.SingleAsync(role => role.Code == "admin");
        var commonRole = await dbContext.AuthRoles.SingleAsync(role => role.Code == "common");
        var rootOrgUnitId = await dbContext.OrgUnits
            .Where(org => org.ParentId == null)
            .Select(org => org.Id)
            .SingleAsync();
        var seedAdmin = await dbContext.SystemUsers
            .Include(user => user.UserRoles)
            .SingleAsync(user => user.Username == "admin");
        dbContext.AuthUserRoles.RemoveRange(seedAdmin.UserRoles);
        dbContext.AuthUserRoles.Add(new AuthUserRole
        {
            UserId = seedAdmin.Id,
            RoleId = commonRole.Id,
            CreatedAt = DateTime.UtcNow
        });

        var first = CreateAdminEntity("concurrent_admin_1");
        var second = CreateAdminEntity("concurrent_admin_2");
        dbContext.SystemUsers.AddRange(first, second);
        await dbContext.SaveChangesAsync();
        dbContext.AuthUserRoles.AddRange(
            new AuthUserRole { UserId = first.Id, RoleId = adminRole.Id, CreatedAt = DateTime.UtcNow },
            new AuthUserRole { UserId = second.Id, RoleId = adminRole.Id, CreatedAt = DateTime.UtcNow });
        dbContext.AuthUserOrgUnits.AddRange(
            new AuthUserOrgUnit { UserId = first.Id, OrgUnitId = rootOrgUnitId, IsPrimary = true, CreatedAt = DateTime.UtcNow },
            new AuthUserOrgUnit { UserId = second.Id, OrgUnitId = rootOrgUnitId, IsPrimary = true, CreatedAt = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();
        return (first.Id, second.Id);

        static SystemUser CreateAdminEntity(string username) => new()
        {
            CompanyId = 1,
            Username = username,
            PasswordHash = "unused-in-test",
            Nickname = username,
            IsActive = true,
            PermissionVersion = 1,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static async Task<int> SeedFutureCoverageScenarioAsync(ApiWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminRole = await dbContext.AuthRoles.SingleAsync(role => role.Code == "admin");
        var rootOrgUnitId = await dbContext.OrgUnits
            .Where(org => org.ParentId == null)
            .Select(org => org.Id)
            .SingleAsync();
        var seedAdminRole = await dbContext.AuthUserRoles
            .Include(link => link.User)
            .SingleAsync(link => link.User.Username == "admin" && link.RoleId == adminRole.Id);
        var handoffAt = DateTime.UtcNow.AddHours(1);
        seedAdminRole.EndAt = handoffAt;

        var futureAdmin = new SystemUser
        {
            CompanyId = seedAdminRole.User.CompanyId,
            Username = "future_coverage_admin",
            PasswordHash = "unused-in-test",
            Nickname = "future coverage admin",
            IsActive = true,
            PermissionVersion = 1,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.SystemUsers.Add(futureAdmin);
        await dbContext.SaveChangesAsync();
        dbContext.AuthUserRoles.Add(new AuthUserRole
        {
            UserId = futureAdmin.Id,
            RoleId = adminRole.Id,
            StartAt = handoffAt,
            CreatedAt = DateTime.UtcNow
        });
        dbContext.AuthUserOrgUnits.Add(new AuthUserOrgUnit
        {
            UserId = futureAdmin.Id,
            OrgUnitId = rootOrgUnitId,
            IsPrimary = true,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
        return futureAdmin.Id;
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
            CreatedAt = DateTime.UtcNow
        };

        dbContext.OrgUnits.Add(child);
        await dbContext.SaveChangesAsync();
        child.Path = $"/{rootOrgUnitId}/{child.Id}/";
        await dbContext.SaveChangesAsync();
        return child.Id;
    }
}
