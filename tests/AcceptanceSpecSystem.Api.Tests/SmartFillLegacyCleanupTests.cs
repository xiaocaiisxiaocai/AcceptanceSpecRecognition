using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Core.Matching.Models;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class SmartFillLegacyCleanupTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SmartFillLegacyCleanupTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task MatchingSimilarityEndpoint_ShouldBeRemoved()
    {
        var response = await _client.PostAsync(
            "/api/matching/similarity",
            ApiClientJson.ToJsonContent(new
            {
                text1 = "A",
                text2 = "B"
            }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MatchingPreviewEndpoint_ShouldBeRemoved()
    {
        var response = await _client.PostAsync(
            "/api/matching/preview",
            ApiClientJson.ToJsonContent(new
            {
                items = new[] { new { rowIndex = 0, project = "P1", specification = "S1" } }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MatchingExecuteEndpoint_ShouldBeRemoved()
    {
        var response = await _client.PostAsync(
            "/api/matching/execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId = 1,
                tableIndex = 0,
                projectColumnIndex = 0,
                specificationColumnIndex = 1,
                acceptanceColumnIndex = 2,
                mappings = new[]
                {
                    new
                    {
                        rowIndex = 1,
                        specId = 1
                    }
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StrictReusePreviewEndpoint_ShouldBeRemoved()
    {
        var response = await _client.PostAsync(
            "/api/matching/reuse/strict/preview",
            ApiClientJson.ToJsonContent(new
            {
                sourceTaskId = "legacy-task",
                targetFileIds = new[] { 1 }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StrictReuseExecuteEndpoint_ShouldBeRemoved()
    {
        var response = await _client.PostAsync(
            "/api/matching/reuse/strict/execute",
            ApiClientJson.ToJsonContent(new
            {
                sourceTaskId = "legacy-task",
                targetFileIds = new[] { 1 }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void SmartFillExecuteDtos_ShouldNotExposeLegacyCompatibilityFields()
    {
        typeof(BatchExecuteFillRequest).GetProperty("HighConfidenceThreshold", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
        typeof(BatchExecuteFillRequest).GetProperty("SourceFileId", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
        typeof(BatchExecuteFillRequest).GetProperty("SourceTableIndex", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
        typeof(FillMapping).GetProperty("SelectedSpecId", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
        typeof(FillMapping).GetProperty("UseLlmSuggestion", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
        typeof(FillMapping).GetProperty("LlmReviewScore", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
        typeof(FillMapping).GetProperty("Acceptance", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
        typeof(FillMapping).GetProperty("Remark", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
    }

    [Fact]
    public void MatchingDtos_ShouldNotKeepLegacySingleTableRequestTypes()
    {
        var repositoryRoot = GetRepositoryRoot();
        var dtoPath = Path.Combine(
            repositoryRoot,
            "src/AcceptanceSpecSystem.Application/Contracts/MatchingDtos.cs".Replace('/', Path.DirectorySeparatorChar));
        var content = File.ReadAllText(dtoPath);

        content.Should().NotContain("public class MatchPreviewRequest",
            "单表预览请求已经从主链移除，不应继续保留旧 DTO");
        content.Should().NotContain("public class MatchPreviewResponse",
            "单表预览响应已经从主链移除，不应继续保留旧 DTO");
        content.Should().NotContain("public class ExecuteFillRequest",
            "单表执行请求已经从主链移除，不应继续保留旧 DTO");
    }

    [Fact]
    public void SmartFillPreviewDtos_ShouldNotExposeLegacySuggestionFields()
    {
        typeof(MatchConfigDto).GetProperty("UseLlmSuggestion", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
        typeof(MatchConfigDto).GetProperty("UseLlmReview", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
        typeof(MatchConfigDto).GetProperty("SuggestNoMatchRows", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
        typeof(MatchConfigDto).GetProperty("LlmSuggestionScoreThreshold", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
        typeof(MatchPreviewItem).GetProperty("LlmSuggestion", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
    }

    [Fact]
    public void MatchConfigDto_ShouldExposeSynchronousAiEquivalenceSwitch_DefaultOn()
    {
        typeof(MatchConfigDto).GetProperty("EnableLlmEquivalenceAdjudication", BindingFlags.Public | BindingFlags.Instance)
            .Should().NotBeNull("同步 AI 等价裁决需要显式开关");

        // 默认开启，让 LLM 等价裁决在未显式配置时自动生效
        var defaultConfig = JsonSerializer.Deserialize<MatchConfigDto>(
            "{}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        defaultConfig.Should().NotBeNull();
        defaultConfig!.EnableLlmEquivalenceAdjudication.Should().BeTrue();

        var disabledConfig = JsonSerializer.Deserialize<MatchConfigDto>(
            """
            {
              "enableLlmEquivalenceAdjudication": false
            }
            """,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        disabledConfig.Should().NotBeNull();
        disabledConfig!.EnableLlmEquivalenceAdjudication.Should().BeFalse();
    }

    [Fact]
    public void SmartFillMainChainDtosAndModels_ShouldNotExposeLegacyLlmReviewFields()
    {
        foreach (var type in new[] { typeof(MatchResultDto), typeof(MatchResult) })
        {
            type.GetProperty("LlmScore", BindingFlags.Public | BindingFlags.Instance).Should().BeNull();
            type.GetProperty("LlmReason", BindingFlags.Public | BindingFlags.Instance).Should().BeNull();
            type.GetProperty("LlmCommentary", BindingFlags.Public | BindingFlags.Instance).Should().BeNull();
            type.GetProperty("IsLlmReviewed", BindingFlags.Public | BindingFlags.Instance).Should().BeNull();
        }
    }

    [Fact]
    public void BatchExecuteFillRequest_ShouldRejectLegacyTopLevelCompatibilityFieldsDuringDeserialization()
    {
        const string payload =
            """
            {
              "fileId": 1,
              "tables": [
                {
                  "tableIndex": 0,
                  "projectColumnIndex": 0,
                  "specificationColumnIndex": 1,
                  "acceptanceColumnIndex": 2,
                  "mappings": [
                    {
                      "rowIndex": 0,
                      "specId": 1,
                      "manualConfirmed": true
                    }
                  ]
                }
              ],
              "sourceFileId": 99
            }
            """;

        var action = () => JsonSerializer.Deserialize<BatchExecuteFillRequest>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        action.Should().Throw<JsonException>()
            .WithMessage("*sourceFileId*");
    }

    [Fact]
    public void BatchExecuteFillRequest_ShouldRejectLegacyTopLevelHighConfidenceThresholdDuringDeserialization()
    {
        const string payload =
            """
            {
              "fileId": 1,
              "tables": [
                {
                  "tableIndex": 0,
                  "projectColumnIndex": 0,
                  "specificationColumnIndex": 1,
                  "acceptanceColumnIndex": 2,
                  "mappings": [
                    {
                      "rowIndex": 0,
                      "specId": 1,
                      "manualConfirmed": true
                    }
                  ]
                }
              ],
              "highConfidenceThreshold": 0.95
            }
            """;

        var action = () => JsonSerializer.Deserialize<BatchExecuteFillRequest>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        action.Should().Throw<JsonException>()
            .WithMessage("*highConfidenceThreshold*");
    }

    [Fact]
    public void FillMapping_ShouldRejectLegacyNestedCompatibilityFieldsDuringDeserialization()
    {
        const string payload =
            """
            {
              "rowIndex": 0,
              "specId": 1,
              "manualConfirmed": true,
              "selectedSpecId": 123,
              "acceptance": "legacy"
            }
            """;

        var action = () => JsonSerializer.Deserialize<FillMapping>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        action.Should().Throw<JsonException>()
            .WithMessage("*selectedSpecId*");
    }

    [Fact]
    public async Task ExecuteFill_ShouldReturnBadRequest_WhenLegacyCompatibilityFieldsArePosted()
    {
        const string payload =
            """
            {
              "fileId": 1,
              "tables": [
                {
                  "tableIndex": 0,
                  "projectColumnIndex": 0,
                  "specificationColumnIndex": 1,
                  "acceptanceColumnIndex": 2,
                  "mappings": [
                    {
                      "rowIndex": 0,
                      "specId": 1,
                      "manualConfirmed": true,
                      "selectedSpecId": 123
                    }
                  ]
                }
              ]
            }
            """;

        using var response = await _client.PostAsync(
            "/api/matching/batch-execute",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BatchExecuteFill_ShouldReturnBadRequest_WhenLegacyTopLevelHighConfidenceThresholdIsPosted()
    {
        const string payload =
            """
            {
              "fileId": 1,
              "highConfidenceThreshold": 0.95,
              "tables": [
                {
                  "tableIndex": 0,
                  "projectColumnIndex": 0,
                  "specificationColumnIndex": 1,
                  "acceptanceColumnIndex": 2,
                  "mappings": [
                    {
                      "rowIndex": 0,
                      "specId": 1
                    }
                  ]
                }
              ]
            }
            """;

        using var response = await _client.PostAsync(
            "/api/matching/batch-execute",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "tests")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("未找到仓库根目录");
    }
}
