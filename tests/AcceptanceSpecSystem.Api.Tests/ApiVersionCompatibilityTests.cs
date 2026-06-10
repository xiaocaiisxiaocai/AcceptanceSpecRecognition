using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class ApiVersionCompatibilityTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiVersionCompatibilityTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task V1ApiPrefix_ShouldReuseExistingApiRoutes()
    {
        var legacyResponse = await _client.GetAsync("/api/ai-services?page=1&pageSize=5");
        var v1Response = await _client.GetAsync("/api/v1/ai-services?page=1&pageSize=5");

        legacyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        v1Response.StatusCode.Should().Be(HttpStatusCode.OK);

        var legacyBody = await legacyResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var v1Body = await v1Response.ReadAsAsync<ApiResponse<JsonElement>>();

        legacyBody.Code.Should().Be(0);
        v1Body.Code.Should().Be(legacyBody.Code);
        v1Body.Message.Should().Be(legacyBody.Message);
    }

    [Fact]
    public async Task AuthRoutes_ShouldKeepLegacyRootPaths()
    {
        var response = await _client.PostAsync(
            "/login",
            ApiClientJson.ToJsonContent(new
            {
                username = "",
                password = ""
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
