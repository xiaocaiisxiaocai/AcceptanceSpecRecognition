using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class ApiRequestValidationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiRequestValidationTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task InvalidDataAnnotationsRequest_ShouldReturnUnifiedApiResponse()
    {
        var response = await _client.PutAsync(
            "/api/embedding-cache-warmup/options",
            ApiClientJson.ToJsonContent(new
            {
                enabled = true,
                runOnStartup = false,
                runAtLocalTime = "03:30",
                intervalHours = 0,
                batchSize = 10,
                maxItemsPerRun = 100
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();

        result.Code.Should().Be(400);
        result.Message.Should().Contain("预热间隔小时数");
        result.Data.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task InvalidColumnMappingPriority_ShouldReturnUnifiedApiResponse()
    {
        var response = await _client.PostAsync(
            "/api/column-mapping-rules",
            ApiClientJson.ToJsonContent(new
            {
                targetField = 1,
                matchMode = 0,
                pattern = "客户",
                priority = 100001,
                enabled = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();

        result.Code.Should().Be(400);
        result.Message.Should().Contain("优先级");
    }
}
