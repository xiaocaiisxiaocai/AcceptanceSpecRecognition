using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AcceptanceSpecSystem.Api.Tests;

public class AuthTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
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
