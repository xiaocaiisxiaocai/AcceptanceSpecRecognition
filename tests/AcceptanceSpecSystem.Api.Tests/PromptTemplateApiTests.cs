using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class PromptTemplateApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public PromptTemplateApiTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetList_ShouldReturnSystemPromptTemplates()
    {
        var response = await _client.GetAsync("/api/prompt-templates");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);

        var items = json.Data.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThanOrEqualTo(3);

        var names = items.EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToArray();

        names.Should().Contain("matching-review");
        names.Should().Contain("import-duplicate-review");
        names.Should().Contain("matching-generate");

        var matchingReview = items.EnumerateArray()
            .First(item => item.GetProperty("name").GetString() == "matching-review");

        matchingReview.GetProperty("isSystem").GetBoolean().Should().BeTrue();
        matchingReview.GetProperty("displayName").GetString().Should().NotBeNullOrWhiteSpace();
        matchingReview.GetProperty("availableVariables").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Preview_WhenTemplateMissesRequiredPlaceholder_ShouldReturnValidationErrors()
    {
        var response = await _client.PostAsync(
            "/api/prompt-templates/preview",
            ApiClientJson.ToJsonContent(new
            {
                scene = "matching-review",
                content = "你是复核助手，仅返回 JSON：{\"score\":0}"
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        json.Data.GetProperty("isValid").GetBoolean().Should().BeFalse();

        var errors = json.Data.GetProperty("errors")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        errors.Should().Contain(error => error!.Contains("sourceProject", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Update_WhenTemplateContainsUnknownPlaceholder_ShouldReject()
    {
        var listResponse = await _client.GetAsync("/api/prompt-templates");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var templateId = listJson.Data.GetProperty("items")
            .EnumerateArray()
            .First(item => item.GetProperty("name").GetString() == "matching-review")
            .GetProperty("id")
            .GetInt32();

        var updateResponse = await _client.PutAsync(
            $"/api/prompt-templates/{templateId}",
            ApiClientJson.ToJsonContent(new
            {
                displayName = "智能填充复核",
                content = "项目：{{sourceProject}}\n规格：{{unknownToken}}\n仅返回 JSON：{\"score\":0,\"reason\":\"\",\"commentary\":\"\"}"
            }));

        updateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var updateJson = await updateResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        updateJson.Code.Should().Be(400);
        updateJson.Message.Should().Contain("unknownToken");
    }
}
