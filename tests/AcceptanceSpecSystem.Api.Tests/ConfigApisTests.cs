using System.Net;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class ConfigApisTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly ApiWebApplicationFactory _factory;

    public ConfigApisTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
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

    [Fact]
    public async Task AiServiceConfig_Create_WithEndpointMissingDoubleSlash_ShouldNormalizeEndpoint()
    {
        var createResp = await _client.PostAsync(
            "/api/ai-services",
            ApiClientJson.ToJsonContent(new
            {
                name = "ollama-endpoint-normalize",
                serviceType = 2,
                purpose = 1,
                priority = 0,
                endpoint = "http:127.0.0.1:11434/api",
                apiKey = "",
                llmModel = "qwen3.5:35b",
                disableThinking = false
            }));

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.ReadAsAsync<ApiResponse<JsonElement>>();
        created.Code.Should().Be(0);
        created.Data.GetProperty("endpoint").GetString().Should().Be("http://127.0.0.1:11434/api");
    }

    [Fact]
    public async Task AiServiceConfig_TestConnection_WithLegacyMalformedOllamaEndpoint_ShouldSucceed()
    {
        var port = GetFreeTcpPort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        string? requestLine = null;
        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            requestLine = $"{context.Request.HttpMethod} {context.Request.RawUrl} HTTP/{context.Request.ProtocolVersion}";

            var responseJson = """
                {
                  "models": [
                    { "name": "qwen3.5:35b" },
                    { "name": "deepseek-r1:32b" }
                  ]
                }
                """;
            var responseBytes = Encoding.UTF8.GetBytes(responseJson);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes);
            context.Response.Close();
        });

        var configId = await CreateLegacyOllamaConfigAsync($"http:127.0.0.1:{port}/api");

        var response = await _client.PostAsync($"/api/ai-services/{configId}/test", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(0);
        result.Data.GetProperty("success").GetBoolean().Should().BeTrue();
        result.Data.GetProperty("message").GetString().Should().Contain("LLM: OK");
        result.Data.GetProperty("message").GetString().Should().Contain("qwen3.5:35b");

        await serverTask;
        requestLine.Should().StartWith("GET /api/tags HTTP/");
    }

    [Fact]
    public async Task AiServiceConfig_TestConnection_WithOllamaMissingConfiguredModel_ShouldFail()
    {
        var port = GetFreeTcpPort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        string? requestLine = null;
        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            requestLine = $"{context.Request.HttpMethod} {context.Request.RawUrl} HTTP/{context.Request.ProtocolVersion}";

            var responseJson = """
                {
                  "models": [
                    { "name": "deepseek-r1:32b" }
                  ]
                }
                """;
            var responseBytes = Encoding.UTF8.GetBytes(responseJson);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes);
            context.Response.Close();
        });

        var configId = await CreateLegacyOllamaConfigAsync($"http:127.0.0.1:{port}/api", "qwen3.5:35b");

        var response = await _client.PostAsync($"/api/ai-services/{configId}/test", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(0);
        result.Data.GetProperty("success").GetBoolean().Should().BeFalse();
        result.Data.GetProperty("message").GetString().Should().Contain("未找到已配置模型");
        result.Data.GetProperty("message").GetString().Should().Contain("qwen3.5:35b");

        await serverTask;
        requestLine.Should().StartWith("GET /api/tags HTTP/");
    }

    [Fact]
    public async Task AiServiceConfig_GetModels_WithLegacyMalformedOllamaEndpoint_ShouldReturnModels()
    {
        var port = GetFreeTcpPort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        string? requestLine = null;
        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            requestLine = $"{context.Request.HttpMethod} {context.Request.RawUrl} HTTP/{context.Request.ProtocolVersion}";

            var responseJson = """
                {
                  "models": [
                    { "name": "qwen3.5:35b" },
                    { "name": "deepseek-r1:32b" }
                  ]
                }
                """;
            var responseBytes = Encoding.UTF8.GetBytes(responseJson);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes);
            context.Response.Close();
        });

        var configId = await CreateLegacyOllamaConfigAsync($"http:127.0.0.1:{port}/api");

        var response = await _client.GetAsync($"/api/ai-services/{configId}/models");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(0);
        result.Data.GetProperty("llmModels").EnumerateArray().Select(item => item.GetString())
            .Should().Contain(["qwen3.5:35b", "deepseek-r1:32b"]);

        await serverTask;
        requestLine.Should().StartWith("GET /api/tags HTTP/");
    }

    private async Task<int> CreateLegacyOllamaConfigAsync(string endpoint, string llmModel = "qwen3.5:35b")
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = new AiServiceConfig
        {
            Name = $"legacy-ollama-{Guid.NewGuid():N}",
            ServiceType = AiServiceType.Ollama,
            Purpose = AiServicePurpose.Llm,
            Priority = 0,
            Endpoint = endpoint,
            LlmModel = llmModel,
            DisableThinking = false,
            CreatedAt = DateTime.Now
        };

        dbContext.AiServiceConfigs.Add(entity);
        await dbContext.SaveChangesAsync();
        return entity.Id;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

