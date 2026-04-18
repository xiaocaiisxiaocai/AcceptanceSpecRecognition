using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Api.Tests;

public class MatchingKnowledgeBackendRemovalTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly ApiWebApplicationFactory _factory;

    public MatchingKnowledgeBackendRemovalTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task MatchingKnowledgeApis_ShouldReturnNotFound()
    {
        (await _client.GetAsync("/api/matching-knowledge")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await _client.PutAsync("/api/matching-knowledge", ApiClientJson.ToJsonContent(new { }))).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await _client.PostAsync("/api/matching-knowledge/clear", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await _client.PostAsync("/api/matching-knowledge/restore-defaults", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await _client.PostAsync("/api/matching-knowledge/drafts/generate", ApiClientJson.ToJsonContent(new { }))).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MatchingKnowledgePermissions_AndNavigationManifest_ShouldBeRemoved()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        var permissionResponse = await _client.GetAsync("/api/auth-permissions");
        permissionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var permissionBody = await permissionResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        permissionBody.Code.Should().Be(0);

        var permissionCodes = permissionBody.Data!.EnumerateArray()
            .Select(item => item.GetProperty("code").GetString())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToList();

        permissionCodes.Should().NotContain("page:config:matching-knowledge");
        permissionCodes.Should().NotContain("btn:matching-knowledge:update");
        permissionCodes.Should().NotContain("btn:matching-knowledge:reset");
        permissionCodes.Should().NotContain("btn:matching-knowledge:generate-draft");

        var manifestPath = Path.Combine(GetRepositoryRoot(), "shared", "navigation", "navigation-manifest.json");
        File.ReadAllText(manifestPath).Should().NotContain("config-matching-knowledge");
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AcceptanceSpecSystem.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("未找到仓库根目录");
    }
}
