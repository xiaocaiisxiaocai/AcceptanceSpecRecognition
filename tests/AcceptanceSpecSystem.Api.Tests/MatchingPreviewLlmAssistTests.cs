using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;

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

        var previewResp = await _client.PostAsync(
            "/api/matching/preview",
            ApiClientJson.ToJsonContent(new
            {
                items = new[] { new { rowIndex = 0, project = "X", specification = "Y" } },
                customerId,
                processId,
                config = new { minScoreThreshold = 0.99 }
            }));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);

        var item = previewJson.Data.GetProperty("items")[0];
        item.TryGetProperty("bestMatch", out var bestMatch).Should().BeTrue();
        bestMatch.ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("noMatchReason").GetString().Should().Be("最佳得分低于阈值");
    }

    [Fact]
    public async Task LlmStream_ShouldEmitReviewAndSuggestion()
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

        var previewResp = await _client.PostAsync(
            "/api/matching/preview",
            ApiClientJson.ToJsonContent(new
            {
                items = new[] { new { rowIndex = 0, project = "P1", specification = "S1" } },
                customerId,
                processId,
                config = new { minScoreThreshold = 0.0 }
            }));
        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var item = previewJson.Data.GetProperty("items")[0];
        var bestMatch = item.GetProperty("bestMatch");
        var baseScore = bestMatch.GetProperty("score").GetDouble();

        var streamRequest = new
        {
            items = new[]
            {
                new
                {
                    rowIndex = 0,
                    sourceProject = "P1",
                    sourceSpecification = "S1",
                    bestMatchSpecId = specId,
                    bestMatchScore = baseScore,
                    scoreDetails = bestMatch.GetProperty("scoreDetails")
                }
            },
            config = new
            {
                useLlmReview = true,
                useLlmSuggestion = true,
                llmSuggestionScoreThreshold = 1.1
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matching/llm-stream")
        {
            Content = ApiClientJson.ToJsonContent(streamRequest)
        };

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await ReadSseEventsAsync(response);
        events.Select(e => e.Event).Should().Contain("review.done");
        events.Select(e => e.Event).Should().Contain("suggestion.done");

        var review = events.First(e => e.Event == "review.done").Data;
        review.GetProperty("score").GetDouble().Should().Be(0.4);
        review.GetProperty("reason").GetString().Should().Be("低分原因");

        var suggest = events.First(e => e.Event == "suggestion.done").Data;
        suggest.GetProperty("acceptance").GetString().Should().Be("LLM-AC");
        suggest.GetProperty("remark").GetString().Should().Be("LLM-REM");
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
