using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Api.Tests;

public class AuthPermissionsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly ApiWebApplicationFactory _factory;

    public AuthPermissionsTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetList_AfterSeed_ShouldIncludeMenuPermissions()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        var response = await _client.GetAsync("/api/auth-permissions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(0);

        var permissionItems = body.Data!.EnumerateArray().ToList();
        permissionItems.Should().Contain(item => item.GetProperty("code").GetString() == "menu:config");
        permissionItems.Should().Contain(item => item.GetProperty("code").GetString() == "menu:rbac");
    }

    [Fact]
    public async Task Login_AfterSeed_ShouldIncludeMenuPermissionsInAuthorizationSnapshot()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        var response = await _client.PostAsync(
            "/login",
            ApiClientJson.ToJsonContent(new { username = "admin", password = ApiWebApplicationFactory.TestAdminPassword }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<JsonElement>();
        var permissions = body.GetProperty("data").GetProperty("permissions")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

        permissions.Should().Contain("menu:config");
        permissions.Should().Contain("menu:rbac");
    }
}
