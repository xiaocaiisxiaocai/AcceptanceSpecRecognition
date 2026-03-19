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
            ApiClientJson.ToJsonContent(new { username = "admin", password = "Admin@123456" }));

        var raw = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"返回内容: {raw}");
        var json = JsonSerializer.Deserialize<JsonElement>(raw);
        json.GetProperty("success").GetBoolean().Should().BeTrue();

        var data = json.GetProperty("data");
        data.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("roles").EnumerateArray().Select(x => x.GetString())
            .Should().Contain("admin");
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
            ApiClientJson.ToJsonContent(new { username = "admin", password = "Admin@123456" }));
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
        refreshData.GetProperty("roles").EnumerateArray().Select(x => x.GetString())
            .Should().Contain("admin");
        refreshData.GetProperty("permissions").EnumerateArray()
            .Select(x => x.GetString())
            .Should().Contain(permission => !string.IsNullOrWhiteSpace(permission));
    }

    [Fact]
    public async Task CommonUser_ShouldAccessAsyncRoutes()
    {
        var loginResp = await _client.PostAsync(
            "/login",
            ApiClientJson.ToJsonContent(new { username = "common", password = "Common@123456" }));
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginJson = await loginResp.ReadAsAsync<JsonElement>();
        var accessToken = loginJson.GetProperty("data").GetProperty("accessToken").GetString();
        accessToken.Should().NotBeNullOrWhiteSpace();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/get-async-routes");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _client.SendAsync(request);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
