using System.Net;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using FluentAssertions;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Api.Tests;

public class MatchingPreviewLlmAssistTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public MatchingPreviewLlmAssistTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Preview_ShouldReturnNoMatchReason_WhenBelowThreshold()
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = "NoMatch-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = "NoMatch-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var specResp = await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = "P1",
                specification = "S1",
                acceptance = "OK-1",
                remark = "R1"
            }));
        specResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewResp = await BatchPreviewTestHelper.PostAsync(
            _client,
            customerId,
            processId,
            new { minScoreThreshold = 0.99 },
            ("X", "Y"));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);

        var item = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0];
        item.TryGetProperty("bestMatch", out var bestMatch).Should().BeTrue();
        bestMatch.ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("noMatchReason").GetString().Should().Be("最佳得分低于阈值");
    }

    [Fact]
    public async Task Preview_ShouldReturnExactShortcutSelectionMetadata()
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = "Rerank-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = "Rerank-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = "项目A",
                specification = "规格A",
                acceptance = "OK-1",
                remark = "R1"
            }));

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = "项目A",
                specification = "规格A",
                acceptance = (string?)null,
                remark = "R2"
            }));

        var previewResp = await BatchPreviewTestHelper.PostAsync(
            _client,
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                recallTopK = 5,
                ambiguityMargin = 0.05
            },
            ("项目A", "规格A"));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);

        var bestMatch = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0].GetProperty("bestMatch");
        bestMatch.TryGetProperty("matchingStrategy", out _).Should().BeFalse();
        bestMatch.GetProperty("selectionMode").GetString().Should().Be("exactShortcut");
        bestMatch.GetProperty("selectionSummary").GetString().Should().Be("项目与规格精确一致，直接命中");
        bestMatch.GetProperty("recalledCandidateCount").GetInt32().Should().Be(1);
        bestMatch.GetProperty("isAmbiguous").GetBoolean().Should().BeFalse();
        bestMatch.GetProperty("embeddingScore").GetDouble().Should().BeGreaterThan(0);
        bestMatch.GetProperty("topCandidates")[0].GetProperty("selectionMode").GetString().Should().Be("exactShortcut");
        bestMatch.GetProperty("topCandidates")[0].GetProperty("selectionSummary").GetString().Should().Be("项目与规格精确一致，直接命中");
        bestMatch.GetProperty("llmEquivalence").GetProperty("confidence").GetDouble().Should().Be(1);
    }

    [Fact]
    public async Task Preview_ShouldReturnAiRerankSelectionMetadata_WhenAiPromotesAnotherCandidate()
    {
        await using var factory = new PromoteLastCandidateRerankApiWebApplicationFactory();
        using var client = factory.CreateClient();

        var customerId = (await (await client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = "AiRerank-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = "AiRerank-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var spec1Response = await client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = "传送速度",
                specification = "速度约 1 米/秒",
                acceptance = "OK-1",
                remark = "R1"
            }));
        spec1Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var spec1Id = (await spec1Response.ReadAsAsync<ApiResponse<JsonElement>>())
            .Data.GetProperty("id").GetInt32();

        var spec2Response = await client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = "传送速度",
                specification = "速度 1000 毫米/秒",
                acceptance = "OK-2",
                remark = "R2"
            }));
        spec2Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var spec2Id = (await spec2Response.ReadAsAsync<ApiResponse<JsonElement>>())
            .Data.GetProperty("id").GetInt32();

        var previewResp = await BatchPreviewTestHelper.PostAsync(
            client,
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                recallTopK = 2,
                ambiguityMargin = 0.05
            },
            ("传送速度", "速度 1m/s"));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);

        var bestMatch = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0].GetProperty("bestMatch");
        bestMatch.GetProperty("selectionMode").GetString().Should().Be("aiRerank");
        bestMatch.GetProperty("selectionSummary").GetString().Should().Contain("AI 从 Top");
        bestMatch.GetProperty("recalledCandidateCount").GetInt32().Should().Be(2);
        bestMatch.GetProperty("topCandidates")[0].GetProperty("selectionMode").GetString().Should().Be("aiRerank");
        bestMatch.GetProperty("topCandidates")[0].GetProperty("specId").GetInt32().Should().Be(bestMatch.GetProperty("specId").GetInt32());
        bestMatch.GetProperty("topCandidates")[0].GetProperty("selectionSummary").GetString().Should().Contain("AI 从 Top");

        var matchedSpecId = bestMatch.GetProperty("specId").GetInt32();
        new[] { spec1Id, spec2Id }.Should().Contain(matchedSpecId);
        matchedSpecId.Should().NotBe(bestMatch.GetProperty("topCandidates")[1].GetProperty("specId").GetInt32());
    }

    [Fact]
    public async Task Preview_ShouldDeduplicateProjectSpecificationVariants_AndKeepHigherPriorityCandidate()
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = "VariantKeep-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = "VariantKeep-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = "项目A",
                specification = "规格A",
                acceptance = "验收版本-1",
                remark = "R1"
            }));

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = "项目A",
                specification = "规格A",
                acceptance = "验收版本-2",
                remark = "R2"
            }));

        var previewResp = await BatchPreviewTestHelper.PostAsync(
            _client,
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                recallTopK = 5,
                ambiguityMargin = 0.05
            },
            ("项目A", "规格A"));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);

        var bestMatch = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0].GetProperty("bestMatch");
        bestMatch.GetProperty("recalledCandidateCount").GetInt32().Should().Be(1);
        bestMatch.GetProperty("topCandidates").GetArrayLength().Should().Be(1);
        bestMatch.GetProperty("acceptance").GetString().Should().Be("验收版本-2");
        bestMatch.GetProperty("remark").GetString().Should().Be("R2");
    }

    [Fact]
    public async Task Preview_WithoutExplicitRecallTopK_ShouldUseEmbeddingServiceDefaults()
    {
        var aiServiceResp = await _client.PostAsync(
            "/api/ai-services",
            ApiClientJson.ToJsonContent(new
            {
                name = $"EmbeddingDefaults-{Guid.NewGuid():N}",
                serviceType = 2,
                purpose = 2,
                priority = 0,
                endpoint = "http://127.0.0.1:11434/api",
                apiKey = "",
                embeddingModel = "nomic-embed-text",
                defaultRecallTopK = 3
            }));
        aiServiceResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var embeddingServiceId = (await aiServiceResp.ReadAsAsync<ApiResponse<JsonElement>>())
            .Data.GetProperty("id").GetInt32();

        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = "ServiceDefault-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = "ServiceDefault-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        for (var i = 1; i <= 4; i++)
        {
            await _client.PostAsync(
                "/api/specs",
                ApiClientJson.ToJsonContent(new
                {
                    customerId,
                    processId,
                    project = "项目A",
                    specification = $"规格A 候选{i}",
                    acceptance = $"OK-{i}",
                    remark = $"R{i}"
                }));
        }

        var previewResp = await BatchPreviewTestHelper.PostAsync(
            _client,
            customerId,
            processId,
            new
            {
                embeddingServiceId,
                minScoreThreshold = 0.0
            },
            ("项目A", "规格A 候选"));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);

        var bestMatch = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0].GetProperty("bestMatch");
        bestMatch.TryGetProperty("matchingStrategy", out _).Should().BeFalse();
        bestMatch.GetProperty("recalledCandidateCount").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Preview_ShouldNotExposeLegacyLlmReviewFields()
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = "LegacyFields-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = "LegacyFields-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = "项目A",
                specification = "规格A",
                acceptance = "OK-1",
                remark = "R1"
            }));

        var previewResp = await BatchPreviewTestHelper.PostAsync(
            _client,
            customerId,
            processId,
            new { minScoreThreshold = 0.0 },
            ("项目A", "规格A"));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var bestMatch = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0].GetProperty("bestMatch");

        bestMatch.TryGetProperty("llmScore", out _).Should().BeFalse();
        bestMatch.TryGetProperty("llmReason", out _).Should().BeFalse();
        bestMatch.TryGetProperty("llmCommentary", out _).Should().BeFalse();
        bestMatch.TryGetProperty("isLlmReviewed", out _).Should().BeFalse();
    }

    [Fact]
    public async Task LlmStream_ShouldEmitReviewLifecycleAndTerminalCompleteEvent()
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = "LlmStreamLifecycle-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = "LlmStreamLifecycle-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = ReviewScenarioSamples.ApprovedBestProject,
                specification = ReviewScenarioSamples.ApprovedBestSpecification,
                acceptance = "验收版本-1",
                remark = "R1"
            }));

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = ReviewScenarioSamples.ApprovedAltProject,
                specification = ReviewScenarioSamples.ApprovedAltBestSpecification,
                acceptance = "验收版本-2",
                remark = "R2"
            }));

        var previewResp = await BatchPreviewTestHelper.PostAsync(
            _client,
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                recallTopK = 5,
                ambiguityMargin = 0.05
            },
            (ReviewScenarioSamples.ApprovedSourceProject, ReviewScenarioSamples.ApprovedSourceSpecification));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var bestMatch = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0].GetProperty("bestMatch");
        bestMatch.GetProperty("isAmbiguous").GetBoolean().Should().BeTrue();

        var streamRequest = new
        {
            customerId,
            processId,
            items = new[]
            {
                new
                {
                    rowIndex = 0,
                    sourceProject = ReviewScenarioSamples.ApprovedSourceProject,
                    sourceSpecification = ReviewScenarioSamples.ApprovedSourceSpecification,
                    bestMatchSpecId = bestMatch.GetProperty("specId").GetInt32(),
                    bestMatchScore = bestMatch.GetProperty("score").GetDouble(),
                    scoreDetails = bestMatch.GetProperty("scoreDetails"),
                    isAmbiguous = true
                }
            },
            config = new { }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matching/llm-stream")
        {
            Content = ApiClientJson.ToJsonContent(streamRequest)
        };

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        var events = await SharedSseTestHelper.ReadSseEventsAsync(response);
        events.Select(e => e.Event).Should().Contain("review.start");
        events.Select(e => e.Event).Should().Contain("review.delta");
        events.Select(e => e.Event).Should().Contain("review.done");
        events.Select(e => e.Event).Should().Contain("stream.complete");

        var complete = events.Last(e => e.Event == "stream.complete").Data;
        complete.GetProperty("totalItems").GetInt32().Should().Be(1);
        complete.GetProperty("reviewTargets").GetInt32().Should().Be(1);
        complete.GetProperty("reviewSuccess").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task LlmStream_ShouldSkipReview_WhenCurrentMatchIsNotAmbiguous()
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = "LlmStream-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = "LlmStream-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var specResp = await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = "P1",
                specification = "S1",
                acceptance = "OK-1",
                remark = "R1"
            }));
        specResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var specId = (await specResp.ReadAsAsync<ApiResponse<JsonElement>>())
            .Data.GetProperty("id").GetInt32();

        var previewResp = await BatchPreviewTestHelper.PostAsync(
            _client,
            customerId,
            processId,
            new { minScoreThreshold = 0.0 },
            ("P1", "S1"));
        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var item = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0];
        var bestMatch = item.GetProperty("bestMatch");
        var baseScore = bestMatch.GetProperty("score").GetDouble();

        var streamRequest = new
        {
            customerId,
            processId,
            items = new[]
            {
                new
                {
                    rowIndex = 0,
                    sourceProject = "P1",
                    sourceSpecification = "S1",
                    bestMatchSpecId = specId,
                    bestMatchScore = baseScore,
                    scoreDetails = bestMatch.GetProperty("scoreDetails"),
                    isAmbiguous = true
                }
            },
            config = new { }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matching/llm-stream")
        {
            Content = ApiClientJson.ToJsonContent(streamRequest)
        };

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await SharedSseTestHelper.ReadSseEventsAsync(response);
        events.Select(e => e.Event).Should().NotContain(eventName => eventName.StartsWith("review.", StringComparison.Ordinal));
        events.Select(e => e.Event).Should().Contain("stream.complete");
    }

    [Fact]
    public async Task LlmStream_WhenEquivalenceVerdictRequiresManualReview_ShouldNotPromoteToAutoApply()
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = "LlmEquivalenceGuard-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = "LlmEquivalenceGuard-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var specResp = await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = "安装要求",
                specification = "最大不可拆部件约为2200",
                acceptance = "OK-1",
                remark = "R1"
            }));
        specResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var specId = (await specResp.ReadAsAsync<ApiResponse<JsonElement>>())
            .Data.GetProperty("id").GetInt32();

        var previewResp = await BatchPreviewTestHelper.PostAsync(
            _client,
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                useLlmEntityResolution = true
            },
            ("安装要求", "最大不可拆部件≈3200"));
        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);

        var previewBestMatch = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0].GetProperty("bestMatch");
        previewBestMatch.GetProperty("specId").GetInt32().Should().Be(specId);
        previewBestMatch.GetProperty("llmEquivalence").GetProperty("verdict").GetString().Should().Be("different");

        var streamRequest = new
        {
            customerId,
            processId,
            items = new[]
            {
                new
                {
                    rowIndex = 0,
                    sourceProject = "安装要求",
                    sourceSpecification = "最大不可拆部件≈3200",
                    bestMatchSpecId = specId,
                    bestMatchScore = 0.91,
                    scoreDetails = new Dictionary<string, double>
                    {
                        ["Embedding"] = 0.91,
                        ["Final"] = 0.91
                    },
                    decision = "autoApply",
                    llmEquivalenceVerdict = "equivalent",
                    isAmbiguous = false
                }
            },
            config = new { }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matching/llm-stream")
        {
            Content = ApiClientJson.ToJsonContent(streamRequest)
        };

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await ReadSseEventsAsync(response);
        events.Select(e => e.Event).Should().Contain("review.done");
        var review = events.First(e => e.Event == "review.done").Data;
        review.GetProperty("decision").GetString().Should().Be("manualReview");
        review.GetProperty("reason").GetString().Should().Contain("AI 等价裁决");
    }

    [Fact]
    public async Task LlmStream_WithForgedClientMatchFields_ShouldUseServerCurrentMatchAndSkipReview()
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = "LlmStreamTrustBoundary-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = "LlmStreamTrustBoundary-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var specResp = await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = "P1",
                specification = "S1",
                acceptance = "OK-1",
                remark = "R1"
            }));
        specResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var specId = (await specResp.ReadAsAsync<ApiResponse<JsonElement>>())
            .Data.GetProperty("id").GetInt32();

        var streamRequest = new
        {
            customerId,
            processId,
            items = new[]
            {
                new
                {
                    rowIndex = 0,
                    sourceProject = "完全不相关项目",
                    sourceSpecification = "完全不相关规格",
                    bestMatchSpecId = specId,
                    bestMatchScore = 0.99,
                    scoreDetails = new Dictionary<string, double>
                    {
                        ["Embedding"] = 0.99,
                        ["Final"] = 0.99
                    },
                    decision = "autoApply",
                    llmEquivalenceVerdict = "equivalent",
                    isAmbiguous = true
                }
            },
            config = new
            {
                minScoreThreshold = 0.99
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matching/llm-stream")
        {
            Content = ApiClientJson.ToJsonContent(streamRequest)
        };

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await SharedSseTestHelper.ReadSseEventsAsync(response);
        events.Select(e => e.Event).Should().NotContain(eventName => eventName.StartsWith("review.", StringComparison.Ordinal));
        events.Select(e => e.Event).Should().Contain("stream.complete");
    }

    [Fact]
    public async Task LlmStream_ShouldIgnoreNoMatchRows_WithoutReviewTargets()
    {
        var streamRequest = new
        {
            items = new[]
            {
                new
                {
                    tableIndex = 0,
                    rowIndex = 0,
                    sourceProject = "无匹配项目",
                    sourceSpecification = "无匹配规格",
                    bestMatchSpecId = (int?)null,
                    bestMatchScore = (double?)null,
                    scoreDetails = (object?)null
                }
            },
            config = new { }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matching/llm-stream")
        {
            Content = ApiClientJson.ToJsonContent(streamRequest)
        };

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await SharedSseTestHelper.ReadSseEventsAsync(response);
        events.Select(e => e.Event).Should().NotContain(eventName => eventName.StartsWith("review.", StringComparison.Ordinal));
        events.Select(e => e.Event).Should().Contain("stream.complete");
    }

    private static async Task<List<SseEvent>> ReadSseEventsAsync(HttpResponseMessage response)
    {
        var events = new List<SseEvent>();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        string? eventName = null;
        var dataBuilder = new StringBuilder();

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line.Replace("event:", "", StringComparison.OrdinalIgnoreCase).Trim();
            }
            else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                dataBuilder.Append(line.Replace("data:", "", StringComparison.OrdinalIgnoreCase).Trim());
            }
            else if (line.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(eventName) && dataBuilder.Length > 0)
                {
                    using var doc = JsonDocument.Parse(dataBuilder.ToString());
                    events.Add(new SseEvent(eventName!, doc.RootElement.Clone()));
                }

                eventName = null;
                dataBuilder.Clear();
            }
        }

        return events;
    }

    private record SseEvent(string Event, JsonElement Data);
}

public class MatchingPreviewEmbeddingFailureTests : IClassFixture<FailingEmbeddingApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public MatchingPreviewEmbeddingFailureTests(FailingEmbeddingApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Preview_WhenCandidateEmbeddingHydrationFails_ShouldReturnExplicitBadRequest()
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = "EmbeddingFailure-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = "EmbeddingFailure-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = "项目A",
                specification = "规格A",
                acceptance = "OK-1",
                remark = "R1"
            }));

        using var response = await BatchPreviewTestHelper.PostAsync(
            _client,
            customerId,
            processId,
            new { minScoreThreshold = 0.0 },
            ("项目A", "规格A"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(400);
        json.Message.Should().Contain("Embedding 服务不可用");
    }
}

public class MatchingPreviewLlmAssistCircuitBreakTests : IClassFixture<FailingReviewLlmApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public MatchingPreviewLlmAssistCircuitBreakTests(FailingReviewLlmApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task LlmStream_WhenCircuitOpensOnCurrentRow_ShouldEmitSingleReviewError()
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = "CircuitSingleError-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = "CircuitSingleError-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = ReviewScenarioSamples.ApprovedBestProject,
                specification = ReviewScenarioSamples.ApprovedBestSpecification,
                acceptance = "验收版本-1",
                remark = "R1"
            }));

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = ReviewScenarioSamples.ApprovedAltProject,
                specification = ReviewScenarioSamples.ApprovedAltBestSpecification,
                acceptance = "验收版本-2",
                remark = "R2"
            }));

        var previewResp = await BatchPreviewTestHelper.PostAsync(
            _client,
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                recallTopK = 5,
                ambiguityMargin = 0.05
            },
            (ReviewScenarioSamples.ApprovedSourceProject, ReviewScenarioSamples.ApprovedSourceSpecification));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var bestMatch = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0].GetProperty("bestMatch");
        bestMatch.GetProperty("isAmbiguous").GetBoolean().Should().BeTrue();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matching/llm-stream")
        {
            Content = ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                items = new[]
                {
                    new
                    {
                        rowIndex = 0,
                        sourceProject = ReviewScenarioSamples.ApprovedSourceProject,
                        sourceSpecification = ReviewScenarioSamples.ApprovedSourceSpecification,
                        bestMatchSpecId = bestMatch.GetProperty("specId").GetInt32(),
                        bestMatchScore = bestMatch.GetProperty("score").GetDouble(),
                        scoreDetails = bestMatch.GetProperty("scoreDetails"),
                        isAmbiguous = true
                    }
                },
                config = new
                {
                    llmRetryCount = 0,
                    llmCircuitBreakFailures = 1
                }
            })
        };

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await SharedSseTestHelper.ReadSseEventsAsync(response);
        events.Count(e => e.Event == "review.error").Should().Be(1);
        events.Select(e => e.Event).Should().Contain("stream.complete");
    }

    [Fact]
    public async Task LlmStream_WhenCircuitOpens_ShouldNotEmitFallbackErrorForExactMatchRows()
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = "CircuitExactSkip-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = "CircuitExactSkip-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = ReviewScenarioSamples.ApprovedBestProject,
                specification = ReviewScenarioSamples.ApprovedBestSpecification,
                acceptance = "验收版本-1",
                remark = "R1"
            }));

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = ReviewScenarioSamples.ApprovedAltProject,
                specification = ReviewScenarioSamples.ApprovedAltBestSpecification,
                acceptance = "验收版本-2",
                remark = "R2"
            }));

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = "P1",
                specification = "S1",
                acceptance = "验收版本-3",
                remark = "R3"
            }));

        var previewResp = await BatchPreviewTestHelper.PostAsync(
            _client,
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                recallTopK = 5,
                ambiguityMargin = 0.05
            },
            (ReviewScenarioSamples.ApprovedSourceProject, ReviewScenarioSamples.ApprovedSourceSpecification),
            ("P1", "S1"));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var previewItems = previewJson.Data.GetProperty("tables")[0].GetProperty("items").EnumerateArray().ToArray();
        var ambiguousMatch = previewItems[0].GetProperty("bestMatch");
        var exactMatch = previewItems[1].GetProperty("bestMatch");
        previewItems[0].GetProperty("bestMatch").GetProperty("isAmbiguous").GetBoolean().Should().BeTrue();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matching/llm-stream")
        {
            Content = ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                items = new object[]
                {
                    new
                    {
                        rowIndex = 0,
                        sourceProject = ReviewScenarioSamples.ApprovedSourceProject,
                        sourceSpecification = ReviewScenarioSamples.ApprovedSourceSpecification,
                        bestMatchSpecId = ambiguousMatch.GetProperty("specId").GetInt32(),
                        bestMatchScore = ambiguousMatch.GetProperty("score").GetDouble(),
                        scoreDetails = ambiguousMatch.GetProperty("scoreDetails"),
                        isAmbiguous = true
                    },
                    new
                    {
                        rowIndex = 1,
                        sourceProject = "P1",
                        sourceSpecification = "S1",
                        bestMatchSpecId = exactMatch.GetProperty("specId").GetInt32(),
                        bestMatchScore = exactMatch.GetProperty("score").GetDouble(),
                        scoreDetails = exactMatch.GetProperty("scoreDetails"),
                        isAmbiguous = false
                    }
                },
                config = new
                {
                    llmParallelism = 1,
                    llmRetryCount = 0,
                    llmCircuitBreakFailures = 1
                }
            })
        };

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await SharedSseTestHelper.ReadSseEventsAsync(response);
        var errorRows = events
            .Where(e => e.Event == "review.error")
            .Select(e => e.Data.GetProperty("rowIndex").GetInt32())
            .ToArray();

        errorRows.Should().Equal(0);
        events.Select(e => e.Event).Should().Contain("stream.complete");
    }

    private static async Task<List<SseEvent>> ReadSseEventsAsync(HttpResponseMessage response)
    {
        var events = new List<SseEvent>();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        string? eventName = null;
        var dataBuilder = new StringBuilder();

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line.Replace("event:", "", StringComparison.OrdinalIgnoreCase).Trim();
            }
            else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                dataBuilder.Append(line.Replace("data:", "", StringComparison.OrdinalIgnoreCase).Trim());
            }
            else if (line.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(eventName) && dataBuilder.Length > 0)
                {
                    using var doc = JsonDocument.Parse(dataBuilder.ToString());
                    events.Add(new SseEvent(eventName!, doc.RootElement.Clone()));
                }

                eventName = null;
                dataBuilder.Clear();
            }
        }

        return events;
    }

    private record SseEvent(string Event, JsonElement Data);
}

public class MatchingPreviewLlmAssistRetryTests : IClassFixture<RetryThenSuccessReviewLlmApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public MatchingPreviewLlmAssistRetryTests(RetryThenSuccessReviewLlmApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task LlmStream_WhenRetrySucceeds_ShouldNotEmitReviewErrorBeforeDone()
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = "RetryReview-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = "RetryReview-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = ReviewScenarioSamples.ApprovedBestProject,
                specification = ReviewScenarioSamples.ApprovedBestSpecification,
                acceptance = "验收版本-1",
                remark = "R1"
            }));

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = ReviewScenarioSamples.ApprovedAltProject,
                specification = ReviewScenarioSamples.ApprovedAltBestSpecification,
                acceptance = "验收版本-2",
                remark = "R2"
            }));

        var previewResp = await BatchPreviewTestHelper.PostAsync(
            _client,
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                recallTopK = 5,
                ambiguityMargin = 0.05
            },
            (ReviewScenarioSamples.ApprovedSourceProject, ReviewScenarioSamples.ApprovedSourceSpecification));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var bestMatch = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0].GetProperty("bestMatch");
        bestMatch.GetProperty("isAmbiguous").GetBoolean().Should().BeTrue();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matching/llm-stream")
        {
            Content = ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                items = new[]
                {
                    new
                    {
                        rowIndex = 0,
                        sourceProject = ReviewScenarioSamples.ApprovedSourceProject,
                        sourceSpecification = ReviewScenarioSamples.ApprovedSourceSpecification,
                        bestMatchSpecId = bestMatch.GetProperty("specId").GetInt32(),
                        bestMatchScore = bestMatch.GetProperty("score").GetDouble(),
                        scoreDetails = bestMatch.GetProperty("scoreDetails"),
                        isAmbiguous = true
                    }
                },
                config = new
                {
                    llmParallelism = 1,
                    llmRetryCount = 1,
                    llmCircuitBreakFailures = 2
                }
            })
        };

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await SharedSseTestHelper.ReadSseEventsAsync(response);
        events.Count(e => e.Event == "review.error").Should().Be(0);
        events.Count(e => e.Event == "review.done").Should().Be(1);
        events.Select(e => e.Event).Should().Contain("stream.complete");
    }
}

public class MatchingPreviewLlmAssistConcurrentCircuitTests : IClassFixture<ConcurrentMixedReviewLlmApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public MatchingPreviewLlmAssistConcurrentCircuitTests(ConcurrentMixedReviewLlmApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task LlmStream_WhenAnotherRowOpensCircuit_ShouldNotEmitErrorAfterDone()
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = "ConcurrentCircuit-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = "ConcurrentCircuit-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = ReviewScenarioSamples.ApprovedBestProject,
                specification = ReviewScenarioSamples.ApprovedBestSpecification,
                acceptance = "验收版本-1",
                remark = "R1"
            }));

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = ReviewScenarioSamples.ApprovedAltProject,
                specification = ReviewScenarioSamples.ApprovedAltBestSpecification,
                acceptance = "验收版本-2",
                remark = "R2"
            }));

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = ReviewScenarioSamples.FailingBestProject,
                specification = ReviewScenarioSamples.FailingBestSpecification,
                acceptance = "验收版本-3",
                remark = "R3"
            }));

        await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = ReviewScenarioSamples.FailingAltProject,
                specification = ReviewScenarioSamples.FailingAltBestSpecification,
                acceptance = "验收版本-4",
                remark = "R4"
            }));

        var previewResp = await BatchPreviewTestHelper.PostAsync(
            _client,
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                recallTopK = 5,
                ambiguityMargin = 0.05
            },
            (ReviewScenarioSamples.ApprovedSourceProject, ReviewScenarioSamples.ApprovedSourceSpecification),
            (ReviewScenarioSamples.FailingSourceProject, ReviewScenarioSamples.FailingSourceSpecification));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var previewItems = previewJson.Data.GetProperty("tables")[0].GetProperty("items").EnumerateArray().ToArray();
        var successMatch = previewItems[0].GetProperty("bestMatch");
        var failMatch = previewItems[1].GetProperty("bestMatch");
        successMatch.GetProperty("isAmbiguous").GetBoolean().Should().BeTrue();
        failMatch.GetProperty("isAmbiguous").GetBoolean().Should().BeTrue();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matching/llm-stream")
        {
            Content = ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                items = new object[]
                {
                    new
                    {
                        rowIndex = 0,
                        sourceProject = ReviewScenarioSamples.ApprovedSourceProject,
                        sourceSpecification = ReviewScenarioSamples.ApprovedSourceSpecification,
                        bestMatchSpecId = successMatch.GetProperty("specId").GetInt32(),
                        bestMatchScore = successMatch.GetProperty("score").GetDouble(),
                        scoreDetails = successMatch.GetProperty("scoreDetails"),
                        isAmbiguous = true
                    },
                    new
                    {
                        rowIndex = 1,
                        sourceProject = ReviewScenarioSamples.FailingSourceProject,
                        sourceSpecification = ReviewScenarioSamples.FailingSourceSpecification,
                        bestMatchSpecId = failMatch.GetProperty("specId").GetInt32(),
                        bestMatchScore = failMatch.GetProperty("score").GetDouble(),
                        scoreDetails = failMatch.GetProperty("scoreDetails"),
                        isAmbiguous = true
                    }
                },
                config = new
                {
                    llmParallelism = 2,
                    llmRetryCount = 0,
                    llmCircuitBreakFailures = 1
                }
            })
        };

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await SharedSseTestHelper.ReadSseEventsAsync(response);
        var row0Events = events
            .Where(e => e.Data.TryGetProperty("rowIndex", out var rowIndex) && rowIndex.GetInt32() == 0)
            .Select(e => e.Event)
            .ToArray();

        row0Events.Should().Contain("review.done");
        row0Events.Should().NotContain("review.error");
        events.Select(e => e.Event).Should().Contain("stream.complete");
    }
}

public sealed class FailingReviewLlmApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(ILlmReviewService));
            services.AddScoped<ILlmReviewService, AlwaysFailReviewService>();
        });
    }
}

internal sealed class AlwaysFailReviewService : ILlmReviewService
{
    public Task<LlmReviewResult?> ReviewAsync(LlmReviewRequest request, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("模拟 review 失败");
    }

    public async IAsyncEnumerable<string> ReviewStreamAsync(
        LlmReviewRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (cancellationToken.IsCancellationRequested)
        {
            yield break;
        }

        throw new InvalidOperationException("模拟 review 失败");
    }

    public bool TryParseReviewResult(string raw, out LlmReviewResult result)
    {
        result = null!;
        return false;
    }
}

public sealed class RetryThenSuccessReviewLlmApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(ILlmReviewService));
            services.AddScoped<ILlmReviewService, RetryThenSuccessReviewService>();
        });
    }
}

internal sealed class RetryThenSuccessReviewService : ILlmReviewService
{
    private const string ReviewJson = "{\"score\":95,\"reason\":\"重试后成功\",\"commentary\":\"第二次成功返回\"}";
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Attempts = new();

    public Task<LlmReviewResult?> ReviewAsync(LlmReviewRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public async IAsyncEnumerable<string> ReviewStreamAsync(
        LlmReviewRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var key = $"{request.SourceProject}|{request.SourceSpecification}";
        var attempt = Attempts.AddOrUpdate(key, 1, (_, current) => current + 1);
        if (attempt == 1)
        {
            throw new InvalidOperationException("模拟首次 review 失败");
        }

        yield return ReviewJson[..12];
        yield return ReviewJson[12..];
    }

    public bool TryParseReviewResult(string raw, out LlmReviewResult result)
    {
        using var doc = JsonDocument.Parse(raw);
        result = new LlmReviewResult
        {
            Score = doc.RootElement.GetProperty("score").GetDouble(),
            Reason = doc.RootElement.GetProperty("reason").GetString(),
            Commentary = doc.RootElement.GetProperty("commentary").GetString()
        };
        return true;
    }
}

public sealed class ConcurrentMixedReviewLlmApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(ILlmReviewService));
            services.AddScoped<ILlmReviewService, ConcurrentMixedReviewService>();
        });
    }
}

internal sealed class ConcurrentMixedReviewService : ILlmReviewService
{
    private const string ReviewJson = "{\"score\":95,\"reason\":\"并行成功\",\"commentary\":\"成功行不应被熔断补发 error\"}";

    public Task<LlmReviewResult?> ReviewAsync(LlmReviewRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public async IAsyncEnumerable<string> ReviewStreamAsync(
        LlmReviewRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (string.Equals(request.SourceProject, ReviewScenarioSamples.FailingSourceProject, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("模拟并行失败触发熔断");
        }

        await Task.Delay(250, cancellationToken);
        yield return ReviewJson[..12];
        yield return ReviewJson[12..];
    }

    public bool TryParseReviewResult(string raw, out LlmReviewResult result)
    {
        using var doc = JsonDocument.Parse(raw);
        result = new LlmReviewResult
        {
            Score = doc.RootElement.GetProperty("score").GetDouble(),
            Reason = doc.RootElement.GetProperty("reason").GetString(),
            Commentary = doc.RootElement.GetProperty("commentary").GetString()
        };
        return true;
    }
}

public sealed class PromoteLastCandidateRerankApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(ILlmCandidateRerankService));
            services.AddScoped<ILlmCandidateRerankService, PromoteLastCandidateRerankService>();
        });
    }
}

internal sealed class PromoteLastCandidateRerankService : ILlmCandidateRerankService
{
    public Task<LlmCandidateRerankResult?> RerankAsync(
        LlmCandidateRerankRequest request,
        CancellationToken cancellationToken = default)
    {
        var selected = request.Candidates.LastOrDefault();
        if (selected == null)
        {
            return Task.FromResult<LlmCandidateRerankResult?>(null);
        }

        return Task.FromResult<LlmCandidateRerankResult?>(new LlmCandidateRerankResult
        {
            SelectedSpecId = selected.SpecId,
            Reason = "测试环境强制改选最后一个候选",
            Confidence = 0.88
        });
    }

    public bool TryParseRerankResult(string raw, out LlmCandidateRerankResult result)
    {
        using var doc = JsonDocument.Parse(raw);
        result = new LlmCandidateRerankResult
        {
            SelectedSpecId = doc.RootElement.GetProperty("selectedSpecId").GetInt32(),
            Reason = doc.RootElement.TryGetProperty("reason", out var reasonElement)
                ? reasonElement.GetString()
                : null,
            Confidence = doc.RootElement.GetProperty("confidence").GetDouble()
        };
        return true;
    }
}

internal static class SharedSseTestHelper
{
    internal static async Task<List<(string Event, JsonElement Data)>> ReadSseEventsAsync(HttpResponseMessage response)
    {
        var events = new List<(string Event, JsonElement Data)>();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        string? eventName = null;
        var dataBuilder = new StringBuilder();

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line.Replace("event:", "", StringComparison.OrdinalIgnoreCase).Trim();
            }
            else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                dataBuilder.Append(line.Replace("data:", "", StringComparison.OrdinalIgnoreCase).Trim());
            }
            else if (line.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(eventName) && dataBuilder.Length > 0)
                {
                    using var doc = JsonDocument.Parse(dataBuilder.ToString());
                    events.Add((eventName!, doc.RootElement.Clone()));
                }

                eventName = null;
                dataBuilder.Clear();
            }
        }

        return events;
    }
}

internal static class BatchPreviewTestHelper
{
    internal static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        int customerId,
        int processId,
        object config,
        params (string Project, string Specification)[] items)
    {
        return await PostInternalAsync(client, customerId, processId, config, previewRequestId: null, items);
    }

    internal static async Task<HttpResponseMessage> PostWithRequestIdAsync(
        HttpClient client,
        int customerId,
        int processId,
        object config,
        string previewRequestId,
        params (string Project, string Specification)[] items)
    {
        return await PostInternalAsync(client, customerId, processId, config, previewRequestId, items);
    }

    private static async Task<HttpResponseMessage> PostInternalAsync(
        HttpClient client,
        int customerId,
        int processId,
        object config,
        string? previewRequestId,
        params (string Project, string Specification)[] items)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(CreateDocxBytes(items)), "file", $"preview-{Guid.NewGuid():N}.docx");

        var uploadResp = await client.PostAsync("/api/documents/upload", multipart);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();

        return await client.PostAsync(
            "/api/matching/batch-preview",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                previewRequestId,
                customerId,
                processId,
                config,
                tables = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        projectColumnIndex = 0,
                        specificationColumnIndex = 1,
                        acceptanceColumnIndex = 2,
                        remarkColumnIndex = 3
                    }
                }
            }));
    }

    private static byte[] CreateDocxBytes((string Project, string Specification)[] items)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            var table = new Table();
            table.AppendChild(new TableProperties(new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

            AppendRow(table, "项目", "规格", "验收", "备注");
            foreach (var item in items)
            {
                AppendRow(table, item.Project, item.Specification, string.Empty, string.Empty);
            }

            mainPart.Document.Body!.Append(table);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static void AppendRow(Table table, string project, string specification, string acceptance, string remark)
    {
        var row = new TableRow();
        foreach (var cell in new[] { project, specification, acceptance, remark })
        {
            row.AppendChild(new TableCell(new Paragraph(new Run(new Text(cell ?? string.Empty)))));
        }

        table.AppendChild(row);
    }
}
