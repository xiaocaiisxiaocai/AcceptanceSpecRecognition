using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class EvidenceDrivenMatchingApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public EvidenceDrivenMatchingApiTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Preview_ShouldExposeDecisionAndEvidenceSummary()
    {
        var (customerId, processId) = await CreateScopeAsync("EvidenceApi");

        await CreateSpecAsync(customerId, processId, "尺寸要求", "宽度等于0.7cm", "RISKY");
        await CreateSpecAsync(customerId, processId, "尺寸要求", "宽度等于0.2cm", "SAFE");

        var previewResp = await _client.PostAsync(
            "/api/matching/preview",
            ApiClientJson.ToJsonContent(new
            {
                items = new[] { new { rowIndex = 0, project = "尺寸要求", specification = "宽度小于0.5cm" } },
                customerId,
                processId,
                config = new
                {
                    matchingStrategy = 2,
                    minScoreThreshold = 0.0,
                    recallTopK = 5,
                    ambiguityMargin = 0.01
                }
            }));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var bestMatch = previewJson.Data.GetProperty("items")[0].GetProperty("bestMatch");
        var item = previewJson.Data.GetProperty("items")[0];

        bestMatch.GetProperty("acceptance").GetString().Should().Be("SAFE");
        bestMatch.GetProperty("decision").GetString().Should().Be("autoApply");
        bestMatch.GetProperty("hasHardConflict").GetBoolean().Should().BeFalse();
        bestMatch.GetProperty("evidenceSummary")[0].GetString().Should().Contain("数值约束相容");
        item.GetProperty("confidenceLevel").GetString().Should().Be("medium");
    }

    [Fact]
    public async Task Preview_WhenOnlyHardConflictExists_ShouldReturnRejectDecisionAndLowConfidence()
    {
        var (customerId, processId) = await CreateScopeAsync("EvidenceRejectApi");

        await CreateSpecAsync(customerId, processId, "尺寸要求", "宽度等于0.7cm", "RISKY");

        var previewResp = await _client.PostAsync(
            "/api/matching/preview",
            ApiClientJson.ToJsonContent(new
            {
                items = new[] { new { rowIndex = 0, project = "尺寸要求", specification = "宽度小于0.5cm" } },
                customerId,
                processId,
                config = new
                {
                    matchingStrategy = 2,
                    minScoreThreshold = 0.0,
                    recallTopK = 5,
                    ambiguityMargin = 0.01
                }
            }));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var item = previewJson.Data.GetProperty("items")[0];
        var bestMatch = item.GetProperty("bestMatch");

        bestMatch.GetProperty("decision").GetString().Should().Be("reject");
        bestMatch.GetProperty("hasHardConflict").GetBoolean().Should().BeTrue();
        item.GetProperty("confidenceLevel").GetString().Should().Be("low");
    }

    [Fact]
    public async Task Preview_WhenScoreBelowHighConfidenceThreshold_ShouldExposeManualReviewAndMediumConfidence()
    {
        var (customerId, processId) = await CreateScopeAsync("EvidenceThresholdApi");

        await CreateSpecAsync(
            customerId,
            processId,
            "设备安装需求",
            "设备供应商在到厂前提供设备的空压位置及流量要求",
            "NEAR");

        var previewResp = await _client.PostAsync(
            "/api/matching/preview",
            ApiClientJson.ToJsonContent(new
            {
                items = new[]
                {
                    new
                    {
                        rowIndex = 0,
                        project = "设备安装需求",
                        specification = "设备供应商在到厂前提供设备的空压位置大小及流量"
                    }
                },
                customerId,
                processId,
                config = new
                {
                    matchingStrategy = 2,
                    minScoreThreshold = 0.0,
                    recallTopK = 5,
                    ambiguityMargin = 0.01,
                    highConfidenceThreshold = 0.95
                }
            }));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var item = previewJson.Data.GetProperty("items")[0];
        var bestMatch = item.GetProperty("bestMatch");

        bestMatch.GetProperty("decision").GetString().Should().Be("manualReview");
        item.GetProperty("confidenceLevel").GetString().Should().Be("medium");
    }

    private async Task<(int customerId, int processId)> CreateScopeAsync(string prefix)
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = $"{prefix}-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = $"{prefix}-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        return (customerId, processId);
    }

    private async Task CreateSpecAsync(int customerId, int processId, string project, string specification, string acceptance)
    {
        var specResp = await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project,
                specification,
                acceptance,
                remark = "R"
            }));

        specResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
