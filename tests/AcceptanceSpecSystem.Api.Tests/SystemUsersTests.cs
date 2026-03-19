using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class SystemUsersTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SystemUsersTests(ApiWebApplicationFactory factory)
    {
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
        var createResp = await _client.PostAsync(
            "/api/system-users",
            ApiClientJson.ToJsonContent(new
            {
                username = "test_user_01",
                password = "User@123456",
                nickname = "测试用户",
                avatar = "",
                roles = new[] { "common" },
                permissions = Array.Empty<string>(),
                isActive = true
            }));
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await createResp.ReadAsAsync<ApiResponse<JsonElement>>();
        created.Code.Should().Be(0);
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
}
