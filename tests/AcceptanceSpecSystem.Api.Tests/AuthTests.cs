using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class AuthTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_WithValidCredential_ShouldReturnJwtPayload()
    {
        var resp = await _client.PostAsync(
            "/login",
            ApiClientJson.ToJsonContent(new { username = "admin", password = ApiWebApplicationFactory.TestAdminPassword }));

        var raw = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"返回内容: {raw}");
        var json = JsonSerializer.Deserialize<JsonElement>(raw);
        json.GetProperty("success").GetBoolean().Should().BeTrue();

        var data = json.GetProperty("data");
        data.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("roleCode").GetString().Should().Be("admin");
        data.TryGetProperty("roles", out _).Should().BeFalse();
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
        var resp = await _client.PostAsync(
            "/refresh-token",
            ApiClientJson.ToJsonContent(new { refreshToken = "invalid-token" }));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var json = await resp.ReadAsAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task RefreshToken_WithValidToken_ShouldReturnLatestAuthorizationSnapshot()
    {
        var loginResp = await _client.PostAsync(
            "/login",
            ApiClientJson.ToJsonContent(new { username = "admin", password = ApiWebApplicationFactory.TestAdminPassword }));
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginJson = await loginResp.ReadAsAsync<JsonElement>();
        var refreshToken = loginJson.GetProperty("data").GetProperty("refreshToken").GetString();
        refreshToken.Should().NotBeNullOrWhiteSpace();

        var refreshResp = await _client.PostAsync(
            "/refresh-token",
            ApiClientJson.ToJsonContent(new { refreshToken }));
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
    public async Task AsyncRoutesEndpoint_ShouldReturnNotFound()
    {
        using var resp = await _client.GetAsync("/get-async-routes");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
