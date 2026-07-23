using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AcceptanceSpecSystem.Api.Tests;

public class AuthTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    [Fact]
    public async Task RefreshSessionCleanup_ShouldDeleteOnlyOneExpiredBatch()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.SystemUsers.Where(user => user.Username == "admin").Select(user => user.Id).SingleAsync();
        var marker = Guid.NewGuid().ToString("N");
        db.AuthRefreshSessions.AddRange(Enumerable.Range(1, 3).Select(index => new AuthRefreshSession
        {
            FamilyId = $"cleanup-{marker}-{index}",
            UserId = userId,
            PermissionVersion = 1,
            TokenHash = $"cleanup-{marker}-{index}",
            ExpiresAt = new DateTime(1990, 1, 1, 0, 0, index, DateTimeKind.Utc)
        }));
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<IAuthRefreshSessionService>();
        var deleted = await service.DeleteExpiredBeforeAsync(
            new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            2,
            CancellationToken.None);

        deleted.Should().Be(2);
        (await db.AuthRefreshSessions.CountAsync(item => item.TokenHash.StartsWith($"cleanup-{marker}")))
            .Should().Be(1);
    }

    [Fact]
    public async Task Login_WithValidCredential_ShouldReturnJwtPayload()
    {
        using var loginRequest = AuthCookieTestHelper.CreateLoginRequest(
            "admin", ApiWebApplicationFactory.TestAdminPassword);
        var resp = await _client.SendAsync(loginRequest);

        var raw = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"返回内容: {raw}");
        var json = JsonSerializer.Deserialize<JsonElement>(raw);
        json.GetProperty("success").GetBoolean().Should().BeTrue();

        var data = json.GetProperty("data");
        data.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.TryGetProperty("refreshToken", out _).Should().BeFalse();
        data.GetProperty("roleCode").GetString().Should().Be("admin");
        data.TryGetProperty("roles", out _).Should().BeFalse();
        var setCookies = resp.Headers.GetValues("Set-Cookie").ToArray();
        setCookies.Should().Contain(value => value.StartsWith("__Host-acceptance-refresh=") &&
                                             value.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
                                             value.Contains("secure", StringComparison.OrdinalIgnoreCase) &&
                                             value.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Login_WhenRateLimitExceeded_ShouldReturnTooManyRequests()
    {
        await using var factory = new LoginRateLimitApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        HttpResponseMessage? lastResponse = null;
        for (var i = 0; i < 3; i++)
        {
            lastResponse?.Dispose();
            using var request = AuthCookieTestHelper.CreateLoginRequest("admin", "wrong-password");
            lastResponse = await client.SendAsync(request);
        }

        using (lastResponse)
        {
            lastResponse!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        }
    }

    [Fact]
    public async Task RefreshToken_WhenRateLimitExceeded_ShouldReturnTooManyRequests()
    {
        await using var factory = new LoginRateLimitApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        HttpResponseMessage? lastResponse = null;
        for (var i = 0; i < 3; i++)
        {
            lastResponse?.Dispose();
            using var request = AuthCookieTestHelper.CreateStateChangingRequest("/refresh-token", "invalid-token", "csrf");
            lastResponse = await client.SendAsync(request);
        }

        using (lastResponse)
        {
            lastResponse!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        }
    }

    [Fact]
    public async Task RefreshToken_WhenDifferentSessionsShareIp_ShouldHaveIndependentBudgets()
    {
        await using var factory = new RefreshRateLimitApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessions = new List<(string RefreshToken, string CsrfToken)>();

        for (var session = 0; session < 3; session++)
        {
            using var loginRequest = AuthCookieTestHelper.CreateLoginRequest(
                "admin",
                ApiWebApplicationFactory.TestAdminPassword);
            using var loginResponse = await client.SendAsync(loginRequest);
            loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            sessions.Add(AuthCookieTestHelper.ReadSessionCookies(loginResponse));
        }

        foreach (var session in sessions)
        {
            using var request = AuthCookieTestHelper.CreateStateChangingRequest(
                "/refresh-token", session.RefreshToken, session.CsrfToken);
            using var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "共享出口 IP 下不同会话不得互相耗尽刷新预算");
        }
    }

    [Fact]
    public async Task AdminPolicy_WhenRoleCommon_ShouldReturnForbidden()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/ai-services");
        req.Headers.Add("X-Test-Role", "common");

        using var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminPolicy_WhenUnauthenticated_ShouldReturnUnauthorized()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/ai-services");
        req.Headers.Add("X-Test-Auth", "anonymous");

        using var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshToken_WithInvalidToken_ShouldReturnUnauthorized()
    {
        using var request = AuthCookieTestHelper.CreateStateChangingRequest("/refresh-token", "invalid-token", "csrf");
        var resp = await _client.SendAsync(request);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var json = await resp.ReadAsAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task RefreshToken_WithValidToken_ShouldReturnLatestAuthorizationSnapshot()
    {
        using var loginRequest = AuthCookieTestHelper.CreateLoginRequest(
            "admin", ApiWebApplicationFactory.TestAdminPassword);
        var loginResp = await _client.SendAsync(loginRequest);
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var (refreshToken, csrfToken) = AuthCookieTestHelper.ReadSessionCookies(loginResp);
        using var refreshRequest = AuthCookieTestHelper.CreateStateChangingRequest("/refresh-token", refreshToken, csrfToken);
        var refreshResp = await _client.SendAsync(refreshRequest);
        refreshResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshJson = await refreshResp.ReadAsAsync<JsonElement>();
        var refreshData = refreshJson.GetProperty("data");
        refreshData.GetProperty("username").GetString().Should().Be("admin");
        refreshData.GetProperty("roleCode").GetString().Should().Be("admin");
        refreshData.TryGetProperty("roles", out _).Should().BeFalse();
        refreshData.GetProperty("permissions").EnumerateArray()
            .Select(x => x.GetString())
            .Should().Contain(permission => !string.IsNullOrWhiteSpace(permission));
    }

    [Fact]
    public async Task RefreshToken_WhenPermissionVersionChanged_ShouldReturnUnauthorized()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var loginRequest = AuthCookieTestHelper.CreateLoginRequest(
            "admin", ApiWebApplicationFactory.TestAdminPassword);
        var loginResp = await client.SendAsync(loginRequest);
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var (refreshToken, csrfToken) = AuthCookieTestHelper.ReadSessionCookies(loginResp);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await dbContext.SystemUsers.SingleAsync(u => u.Username == "admin");
            user.PermissionVersion += 1;
            await dbContext.SaveChangesAsync();
        }

        using var refreshRequest = AuthCookieTestHelper.CreateStateChangingRequest("/refresh-token", refreshToken, csrfToken);
        var refreshResp = await client.SendAsync(refreshRequest);

        refreshResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var refreshJson = await refreshResp.ReadAsAsync<JsonElement>();
        refreshJson.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Login_WhenRoleOrOrganizationExpiresSoon_ShouldClampAccessTokenExpiry()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var authorizationBoundary = DateTime.UtcNow.AddMinutes(5);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var common = await dbContext.SystemUsers
                .Include(user => user.UserRoles)
                .Include(user => user.UserOrgUnits)
                .SingleAsync(user => user.Username == "common");
            common.UserRoles.Single().EndAt = authorizationBoundary;
            common.UserOrgUnits.Single().EndAt = authorizationBoundary.AddMinutes(1);
            await dbContext.SaveChangesAsync();
        }

        using var loginRequest = AuthCookieTestHelper.CreateLoginRequest(
            "common", ApiWebApplicationFactory.TestCommonPassword);
        using var loginResponse = await client.SendAsync(loginRequest);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await loginResponse.ReadAsAsync<JsonElement>();
        var expires = body.GetProperty("data").GetProperty("expires").GetDateTime();

        expires.Should().BeOnOrBefore(authorizationBoundary.AddSeconds(1));
    }

    [Fact]
    public async Task RefreshToken_WhenRoleAndOrganizationHaveExpired_ShouldRevokeSessionFamily()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var loginRequest = AuthCookieTestHelper.CreateLoginRequest(
            "common", ApiWebApplicationFactory.TestCommonPassword);
        using var loginResponse = await client.SendAsync(loginRequest);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var (refreshToken, csrfToken) = AuthCookieTestHelper.ReadSessionCookies(loginResponse);

        int commonUserId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var common = await dbContext.SystemUsers
                .Include(user => user.UserRoles)
                .Include(user => user.UserOrgUnits)
                .SingleAsync(user => user.Username == "common");
            commonUserId = common.Id;
            var expiredAt = DateTime.UtcNow.AddSeconds(-1);
            common.UserRoles.Single().EndAt = expiredAt;
            common.UserOrgUnits.Single().EndAt = expiredAt;
            await dbContext.SaveChangesAsync();
        }

        using var refreshRequest = AuthCookieTestHelper.CreateStateChangingRequest(
            "/refresh-token", refreshToken, csrfToken);
        using var refreshResponse = await client.SendAsync(refreshRequest);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDbContext.AuthRefreshSessions
                .AnyAsync(session => session.UserId == commonUserId &&
                                     session.Status == AcceptanceSpecSystem.Data.Entities.AuthRefreshSessionStatus.Active))
            .Should().BeFalse();
    }

    [Fact]
    public async Task AsyncRoutesEndpoint_ShouldReturnNotFound()
    {
        using var resp = await _client.GetAsync("/get-async-routes");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

public sealed class LoginRateLimitApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiRateLimits:Login:PermitLimit"] = "2",
                ["ApiRateLimits:Login:WindowSeconds"] = "60",
                ["ApiRateLimits:Login:QueueLimit"] = "0",
                ["ApiRateLimits:RefreshToken:PermitLimit"] = "2",
                ["ApiRateLimits:RefreshToken:WindowSeconds"] = "60",
                ["ApiRateLimits:RefreshToken:QueueLimit"] = "0"
            });
        });
    }
}

public sealed class RefreshRateLimitApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiRateLimits:Login:PermitLimit"] = "100",
                ["ApiRateLimits:RefreshToken:PermitLimit"] = "2",
                ["ApiRateLimits:RefreshToken:WindowSeconds"] = "60",
                ["ApiRateLimits:RefreshToken:QueueLimit"] = "0"
            });
        });
    }
}
