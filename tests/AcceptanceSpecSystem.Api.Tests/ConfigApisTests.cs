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

    [Fact]
    public async Task AiServiceConfig_DisableThinking_ShouldPersist()
    {
        var createResp = await _client.PostAsync(
            "/api/ai-services",
            ApiClientJson.ToJsonContent(new
            {
                name = "ollama-test",
                serviceType = 2,
                purpose = 1,
                priority = 0,
                endpoint = "http://127.0.0.1:11434/api",
                apiKey = "",
                llmModel = "qwen3.5:35b",
                disableThinking = true
            }));

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.ReadAsAsync<ApiResponse<JsonElement>>();
        created.Code.Should().Be(0);
        created.Data.GetProperty("disableThinking").GetBoolean().Should().BeTrue();

        var id = created.Data.GetProperty("id").GetInt32();
        var getResp = await _client.GetAsync($"/api/ai-services/{id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await getResp.ReadAsAsync<ApiResponse<JsonElement>>();
        detail.Code.Should().Be(0);
        detail.Data.GetProperty("disableThinking").GetBoolean().Should().BeTrue();
    }
}

