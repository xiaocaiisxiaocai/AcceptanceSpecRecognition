using System.Net;
using System.Text.Json;
using FluentAssertions;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;

namespace AcceptanceSpecSystem.Api.Tests;

public class MatchingPreviewScoreDetailsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public MatchingPreviewScoreDetailsTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Preview_ShouldIncludeScoreDetails_ForEnabledAlgorithms()
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = "ScoreDetails-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = "ScoreDetails-P" })))
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
                items = new[] { new { rowIndex = 0, project = "P1", specification = "S1" } },
                customerId,
                processId,
                config = new
                {
                    useLevenshtein = true,
                    useJaccard = true,
                    useCosine = true,
                    minScoreThreshold = 0.0
                }
            }));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);
        previewJson.Data.ValueKind.Should().NotBe(JsonValueKind.Undefined);

        var item = previewJson.Data.GetProperty("items")[0];
        item.TryGetProperty("bestMatch", out var bestMatch).Should().BeTrue();
        bestMatch.ValueKind.Should().NotBe(JsonValueKind.Null);

        var scoreDetails = bestMatch.GetProperty("scoreDetails");
        scoreDetails.TryGetProperty("Levenshtein", out _).Should().BeTrue();
        scoreDetails.TryGetProperty("Jaccard", out _).Should().BeTrue();
        scoreDetails.TryGetProperty("Cosine", out _).Should().BeTrue();
    }
}
