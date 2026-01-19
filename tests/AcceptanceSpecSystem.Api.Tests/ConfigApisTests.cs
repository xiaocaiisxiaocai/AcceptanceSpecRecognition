using System.Net;
using System.Text.Json;
using FluentAssertions;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;

namespace AcceptanceSpecSystem.Api.Tests;

public class ConfigApisTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ConfigApisTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task TextProcessingConfig_GetAndSave_ShouldWork()
    {
        var getResp = await _client.GetAsync("/api/text-processing/config");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cfg = await getResp.ReadAsAsync<ApiResponse<JsonElement>>();
        cfg.Code.Should().Be(0);
        cfg.Data.ValueKind.Should().NotBe(JsonValueKind.Undefined);

        var putResp = await _client.PutAsync(
            "/api/text-processing/config",
            ApiClientJson.ToJsonContent(new
            {
                enableChineseConversion = false,
                conversionMode = 0,
                enableSynonym = true,
                enableOkNgConversion = true,
                okStandardFormat = "OK",
                ngStandardFormat = "NG",
                enableKeywordHighlight = false,
                highlightColorHex = "#FFFF00"
            }));
        putResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await putResp.ReadAsAsync<ApiResponse<JsonElement>>();
        saved.Code.Should().Be(0);
        saved.Data.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        saved.Data.GetProperty("enableSynonym").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task PromptTemplates_Default_ShouldWork()
    {
        var resp = await _client.GetAsync("/api/prompt-templates/default");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var tpl = await resp.ReadAsAsync<ApiResponse<JsonElement>>();
        tpl.Code.Should().Be(0);
        tpl.Data.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        tpl.Data.GetProperty("id").GetInt32().Should().BeGreaterThan(0);
    }
}

