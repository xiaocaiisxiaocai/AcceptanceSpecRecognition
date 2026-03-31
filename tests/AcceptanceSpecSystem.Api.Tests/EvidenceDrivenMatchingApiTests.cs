using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.DTOs;
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

    [Fact]
    public async Task Preview_ShouldExposeStructuredIssues()
    {
        var (customerId, processId) = await CreateScopeAsync("IssueApi");

        await CreateSpecAsync(customerId, processId, "电压要求", "电压等于2.4V", "NG");

        var previewResp = await _client.PostAsync(
            "/api/matching/preview",
            ApiClientJson.ToJsonContent(new
            {
                items = new[] { new { rowIndex = 0, project = "电压要求", specification = "电压等于24V" } },
                customerId,
                processId,
                config = new
                {
                    matchingStrategy = 2,
                    minScoreThreshold = 0.0,
                    recallTopK = 3
                }
            }));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var bestMatch = previewJson.Data.GetProperty("items")[0].GetProperty("bestMatch");
        var issues = bestMatch.GetProperty("issues");

        issues.GetArrayLength().Should().BeGreaterThan(0);
        issues[0].GetProperty("code").GetString().Should().Be("numeric_value_conflict");
        issues[0].GetProperty("fieldName").GetString().Should().Be("电压");
        issues[0].GetProperty("sourceValue").GetString().Should().Be("24V");
        issues[0].GetProperty("candidateValue").GetString().Should().Be("2.4V");
    }

    [Fact]
    public async Task Preview_WhenVoltageAlternativesContainTypo_ShouldExposeActualConflictingValues()
    {
        var (customerId, processId) = await CreateScopeAsync("VoltageAlternativeIssueApi");

        await CreateSpecAsync(
            customerId,
            processId,
            "水/电/气",
            "电力规格要求: 380V三相/50HZ或220V/50HZ；气压需求≤6kg/cm3",
            "NG");

        var previewResp = await _client.PostAsync(
            "/api/matching/preview",
            ApiClientJson.ToJsonContent(new
            {
                items = new[]
                {
                    new
                    {
                        rowIndex = 0,
                        project = "水/电/气",
                        specification = "电力规格要求: 380V三相/50HZ或22V/50HZ；气压需求≤6kg/cm3"
                    }
                },
                customerId,
                processId,
                config = new
                {
                    matchingStrategy = 2,
                    minScoreThreshold = 0.0,
                    recallTopK = 3
                }
            }));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var bestMatch = previewJson.Data.GetProperty("items")[0].GetProperty("bestMatch");
        var issues = bestMatch.GetProperty("issues");

        issues.EnumerateArray().Should().Contain(issue =>
            issue.GetProperty("code").GetString() == "numeric_value_conflict" &&
            issue.GetProperty("fieldName").GetString() == "电压" &&
            issue.GetProperty("sourceValue").GetString() == "22V" &&
            issue.GetProperty("candidateValue").GetString() == "220V" &&
            issue.GetProperty("message").GetString()!.Contains("22V", StringComparison.OrdinalIgnoreCase) &&
            issue.GetProperty("message").GetString()!.Contains("220V", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Preview_WhenLlmEntityResolutionFindsConflict_ShouldExposeEntityIssues()
    {
        var (customerId, processId) = await CreateScopeAsync("EntityIssueApi");

        await CreateSpecAsync(customerId, processId, "设备要求", "BetaMotion 设备需安装防护罩", "NG");

        var previewResp = await _client.PostAsync(
            "/api/matching/preview",
            ApiClientJson.ToJsonContent(new
            {
                items = new[]
                {
                    new
                    {
                        rowIndex = 0,
                        project = "设备要求",
                        specification = "AlphaTech 设备需安装防护罩"
                    }
                },
                customerId,
                processId,
                config = new
                {
                    matchingStrategy = 2,
                    minScoreThreshold = 0.0,
                    recallTopK = 3,
                    useLlmEntityResolution = true
                }
            }));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var bestMatch = previewJson.Data.GetProperty("items")[0].GetProperty("bestMatch");
        var issues = bestMatch.GetProperty("issues");

        issues.EnumerateArray().Should().Contain(issue =>
            issue.GetProperty("code").GetString() == "entity_conflict" &&
            issue.GetProperty("sourceValue").GetString() == "AlphaTech" &&
            issue.GetProperty("candidateValue").GetString() == "BetaMotion");
        bestMatch.GetProperty("decision").GetString().Should().Be("reject");
    }

    [Fact]
    public async Task Preview_WhenLlmEntityResolutionFindsAliasSame_ShouldExposeEntityEvidence()
    {
        var (customerId, processId) = await CreateScopeAsync("EntityAliasApi");

        await CreateSpecAsync(customerId, processId, "设备要求", "阿尔法科技设备需安装防护罩", "OK");

        var previewResp = await _client.PostAsync(
            "/api/matching/preview",
            ApiClientJson.ToJsonContent(new
            {
                items = new[]
                {
                    new
                    {
                        rowIndex = 0,
                        project = "设备要求",
                        specification = "AlphaTech 设备需安装防护罩"
                    }
                },
                customerId,
                processId,
                config = new
                {
                    matchingStrategy = 2,
                    minScoreThreshold = 0.0,
                    recallTopK = 3,
                    useLlmEntityResolution = true
                }
            }));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var bestMatch = previewJson.Data.GetProperty("items")[0].GetProperty("bestMatch");
        var entities = bestMatch.GetProperty("entities");

        bestMatch.GetProperty("decision").GetString().Should().Be("autoApply");
        entities.EnumerateArray().Should().Contain(entity =>
            entity.GetProperty("relation").GetString() == "aliasSame" &&
            entity.GetProperty("sourceValue").GetString() == "AlphaTech" &&
            entity.GetProperty("candidateValue").GetString() == "阿尔法科技");
    }

    [Fact]
    public async Task Preview_WhenLlmEntityResolutionReturnsUnknown_ShouldExposeEntityUnknownIssue()
    {
        var (customerId, processId) = await CreateScopeAsync("EntityUnknownApi");

        await CreateSpecAsync(customerId, processId, "设备要求", "新境科技设备需安装防护罩", "CHECK");

        var previewResp = await _client.PostAsync(
            "/api/matching/preview",
            ApiClientJson.ToJsonContent(new
            {
                items = new[]
                {
                    new
                    {
                        rowIndex = 0,
                        project = "设备要求",
                        specification = "XJTech 设备需安装防护罩"
                    }
                },
                customerId,
                processId,
                config = new
                {
                    matchingStrategy = 2,
                    minScoreThreshold = 0.0,
                    recallTopK = 3,
                    useLlmEntityResolution = true
                }
            }));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var bestMatch = previewJson.Data.GetProperty("items")[0].GetProperty("bestMatch");
        var issues = bestMatch.GetProperty("issues");

        bestMatch.GetProperty("decision").GetString().Should().Be("manualReview");
        issues.EnumerateArray().Should().Contain(issue =>
            issue.GetProperty("code").GetString() == "entity_unknown" &&
            issue.GetProperty("sourceValue").GetString() == "XJTech" &&
            issue.GetProperty("candidateValue").GetString() == "新境科技");
    }

    [Fact]
    public async Task Preview_WhenSingleStageEnablesLlmEntityResolution_ShouldReturnBadRequest()
    {
        var (customerId, processId) = await CreateScopeAsync("EntitySingleStageApi");

        await CreateSpecAsync(customerId, processId, "设备要求", "AlphaTech 设备需安装防护罩", "OK");

        var previewResp = await _client.PostAsync(
            "/api/matching/preview",
            ApiClientJson.ToJsonContent(new
            {
                items = new[]
                {
                    new
                    {
                        rowIndex = 0,
                        project = "设备要求",
                        specification = "AlphaTech 设备需安装防护罩"
                    }
                },
                customerId,
                processId,
                config = new
                {
                    matchingStrategy = 1,
                    minScoreThreshold = 0.0,
                    useLlmEntityResolution = true
                }
            }));

        previewResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errorJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        errorJson.Message.Should().Contain("LLM 实体判别");
        errorJson.Message.Should().Contain("多阶段");
    }

    [Fact]
    public void MatchConfigDto_ShouldExposeLlmEntityResolutionSettings()
    {
        var config = new MatchConfigDto();

        config.UseLlmEntityResolution.Should().BeFalse();
        config.LlmEntityResolutionTopCandidates.Should().Be(3);
        config.LlmEntityPositiveConfidenceThreshold.Should().Be(0.85);
        config.LlmEntityConflictReviewConfidenceThreshold.Should().Be(0.7);
        config.LlmEntityConflictRejectConfidenceThreshold.Should().Be(0.9);
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
