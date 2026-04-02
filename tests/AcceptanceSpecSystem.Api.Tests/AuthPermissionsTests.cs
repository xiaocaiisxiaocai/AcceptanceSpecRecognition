using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
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
    public async Task GetList_AfterSeed_ShouldExposeMatchingKnowledgePermissions_AndHideLegacyPermissions()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        var response = await _client.GetAsync("/api/auth-permissions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(0);

        var permissionCodes = body.Data!.EnumerateArray()
            .Select(item => item.GetProperty("code").GetString())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToList();

        permissionCodes.Should().Contain("page:config:matching-knowledge");
        permissionCodes.Should().Contain("btn:matching-knowledge:update");
        permissionCodes.Should().Contain("btn:matching-knowledge:reset");
        permissionCodes.Should().Contain("btn:matching-knowledge:generate-draft");
        permissionCodes.Should().NotContain("page:config:text-processing");
        permissionCodes.Should().NotContain("page:other:synonyms");
        permissionCodes.Should().NotContain("page:other:keywords");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var legacyPermissions = await dbContext.AuthPermissions
            .Where(permission =>
                permission.Code == "page:config:text-processing" ||
                permission.Code == "page:other:synonyms" ||
                permission.Code == "page:other:keywords")
            .ToListAsync();

        legacyPermissions.Should().OnlyContain(permission => !permission.IsActive);
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

    [Fact]
    public async Task Login_AfterSeed_ShouldIncludeMatchingKnowledgePermissionsInAuthorizationSnapshot()
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

        permissions.Should().Contain("page:config:matching-knowledge");
        permissions.Should().Contain("btn:matching-knowledge:update");
        permissions.Should().Contain("btn:matching-knowledge:reset");
        permissions.Should().Contain("btn:matching-knowledge:generate-draft");
        permissions.Should().NotContain("page:config:text-processing");
        permissions.Should().NotContain("page:other:synonyms");
        permissions.Should().NotContain("page:other:keywords");
    }

    [Fact]
    public async Task Login_CommonUserAfterSeed_ShouldIncludeMainWorkflowButtonPermissions()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        var response = await _client.PostAsync(
            "/login",
            ApiClientJson.ToJsonContent(new
            {
                username = "common",
                password = ApiWebApplicationFactory.TestCommonPassword
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<JsonElement>();
        var permissions = body.GetProperty("data").GetProperty("permissions")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

        permissions.Should().Contain("btn:document:upload");
        permissions.Should().Contain("btn:document:import");
        permissions.Should().Contain("btn:excel-document:import");
        permissions.Should().Contain("btn:file-compare:upload");
        permissions.Should().Contain("btn:file-compare:preview");
        permissions.Should().Contain("btn:file-compare:download");
        permissions.Should().Contain("btn:matching:preview");
        permissions.Should().Contain("btn:matching:preview-batch");
        permissions.Should().Contain("btn:matching:download");
        permissions.Should().Contain("btn:matching-fill:llm-stream");
        permissions.Should().Contain("btn:matching-fill:execute");
        permissions.Should().Contain("btn:matching-fill:execute-batch");
        permissions.Should().NotContain("btn:matching:llm-stream");
        permissions.Should().NotContain("api:matching:execute");
        permissions.Should().NotContain("api:matching:execute-batch");
        permissions.Should().NotContain("api:matching:llm-stream");
    }

    [Fact]
    public async Task Login_CommonUserAfterSeed_ShouldIncludeBatchReplyPermissions()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        var response = await _client.PostAsync(
            "/login",
            ApiClientJson.ToJsonContent(new
            {
                username = "common",
                password = ApiWebApplicationFactory.TestCommonPassword
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<JsonElement>();
        var permissions = body.GetProperty("data").GetProperty("permissions")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

        permissions.Should().Contain("menu:batch-reply");
        permissions.Should().Contain("page:batch-reply:index");
        permissions.Should().Contain("api:batch-reply:upload");
        permissions.Should().Contain("api:batch-reply:upload-source");
        permissions.Should().Contain("api:batch-reply:preview");
        permissions.Should().Contain("api:batch-reply:execute");
        permissions.Should().Contain("api:batch-reply:download");
        permissions.Should().Contain("btn:batch-reply:preview");
        permissions.Should().Contain("btn:batch-reply:execute");
    }
}
