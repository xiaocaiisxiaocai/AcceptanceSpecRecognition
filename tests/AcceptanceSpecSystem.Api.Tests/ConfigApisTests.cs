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
    public void AiServiceRequests_ShouldDefaultRecallTopKToTwo()
    {
        var createRequest = new AcceptanceSpecSystem.Api.DTOs.CreateAiServiceRequest();
        var updateRequest = new AcceptanceSpecSystem.Api.DTOs.UpdateAiServiceRequest();

        createRequest.DefaultRecallTopK.Should().Be(2);
        updateRequest.DefaultRecallTopK.Should().Be(2);
    }

    [Fact]
    public async Task LegacyTextProcessingApis_ShouldReturnNotFound()
    {
        var response = await _client.GetAsync("/api/text-processing/config");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await _client.GetAsync("/api/synonyms")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await _client.DeleteAsync("/api/synonyms/1")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync("/api/keywords")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PromptTemplates_DefaultEndpoint_ShouldReturnNotFound()
    {
        var resp = await _client.GetAsync("/api/prompt-templates/default");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
    public async Task AiServiceConfig_DefaultRecallTopK_ShouldPersistWithoutLegacyMatchingStrategyField()
    {
        var createResp = await _client.PostAsync(
            "/api/ai-services",
            ApiClientJson.ToJsonContent(new
            {
                name = $"embedding-defaults-{Guid.NewGuid():N}",
                serviceType = 2,
                purpose = 2,
                priority = 0,
                endpoint = "http://127.0.0.1:11434/api",
                apiKey = "",
                embeddingModel = "nomic-embed-text",
                defaultRecallTopK = 3
            }));

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.ReadAsAsync<ApiResponse<JsonElement>>();
        created.Code.Should().Be(0);
        created.Data.GetProperty("defaultRecallTopK").GetInt32().Should().Be(3);
        created.Data.TryGetProperty("defaultMatchingStrategy", out _).Should().BeFalse();

        var id = created.Data.GetProperty("id").GetInt32();
        var getResp = await _client.GetAsync($"/api/ai-services/{id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await getResp.ReadAsAsync<ApiResponse<JsonElement>>();
        detail.Code.Should().Be(0);
        detail.Data.GetProperty("defaultRecallTopK").GetInt32().Should().Be(3);
        detail.Data.TryGetProperty("defaultMatchingStrategy", out _).Should().BeFalse();
    }

    [Fact]
    public async Task AiServiceConfig_GetById_ShouldMaskStoredApiKey()
    {
        const string rawApiKey = "sk-secret-key-1234567890";

        var createResp = await _client.PostAsync(
            "/api/ai-services",
            ApiClientJson.ToJsonContent(new
            {
                name = $"masked-{Guid.NewGuid():N}",
                serviceType = 0,
                purpose = 1,
                priority = 0,
                endpoint = "https://api.openai.com/v1",
                apiKey = rawApiKey,
                llmModel = "gpt-4o-mini",
                disableThinking = false
            }));

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var id = created.Data.GetProperty("id").GetInt32();

        var getResp = await _client.GetAsync($"/api/ai-services/{id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await getResp.ReadAsAsync<ApiResponse<JsonElement>>();
        detail.Code.Should().Be(0);
        detail.Data.GetProperty("hasApiKey").GetBoolean().Should().BeTrue();
        detail.Data.GetProperty("apiKey").GetString().Should().NotBe(rawApiKey);
        detail.Data.GetProperty("apiKey").GetString().Should().Contain("***");
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

    [Fact]
    public async Task AiServiceConfig_GetById_WithLegacyCombinedPurpose_ShouldNormalizePurposeForRead()
    {
        var configId = await CreateLegacyCombinedPurposeConfigAsync(
            endpoint: "https://api.openai.com/v1",
            serviceType: AiServiceType.OpenAI,
            llmModel: "gpt-4o-mini",
            embeddingModel: null);

        var response = await _client.GetAsync($"/api/ai-services/{configId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(0);
        result.Data.GetProperty("purpose").GetInt32().Should().Be((int)AiServicePurpose.Llm);
        result.Data.GetProperty("llmModel").GetString().Should().Be("gpt-4o-mini");
    }

    [Fact]
    public async Task AiServiceConfig_TestConnection_WithLegacyCombinedPurposeAndSingleModel_ShouldUseNormalizedPurpose()
    {
        var port = GetFreeTcpPort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var responseJson = """
                {
                  "models": [
                    { "name": "qwen3.5:35b" }
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

        var configId = await CreateLegacyCombinedPurposeConfigAsync(
            endpoint: $"http:127.0.0.1:{port}/api",
            serviceType: AiServiceType.Ollama,
            llmModel: "qwen3.5:35b",
            embeddingModel: null);

        var response = await _client.PostAsync($"/api/ai-services/{configId}/test", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(0);
        result.Data.GetProperty("success").GetBoolean().Should().BeTrue();
        result.Data.GetProperty("message").GetString().Should().NotContain("LLM 与 Embedding 需要分开配置");

        await serverTask;
    }

    [Fact]
    public async Task AiServiceConfig_GetById_WithLegacyDualPurposeAndBothModels_ShouldExposeBothModels()
    {
        var configId = await CreateLegacyCombinedPurposeConfigAsync(
            endpoint: "https://api.openai.com/v1",
            serviceType: AiServiceType.OpenAI,
            llmModel: "gpt-4o-mini",
            embeddingModel: "text-embedding-3-small");

        var response = await _client.GetAsync($"/api/ai-services/{configId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(0);
        result.Data.GetProperty("purpose").GetInt32().Should().Be((int)(AiServicePurpose.Llm | AiServicePurpose.Embedding));
        result.Data.GetProperty("llmModel").GetString().Should().Be("gpt-4o-mini");
        result.Data.GetProperty("embeddingModel").GetString().Should().Be("text-embedding-3-small");
    }

    [Fact]
    public async Task AiServiceConfig_Update_WithLegacyDualPurposeAndBothModels_ShouldRejectInsteadOfDroppingHiddenModel()
    {
        var configId = await CreateLegacyCombinedPurposeConfigAsync(
            endpoint: "https://api.openai.com/v1",
            serviceType: AiServiceType.OpenAI,
            llmModel: "gpt-4o-mini",
            embeddingModel: "text-embedding-3-small");

        var response = await _client.PutAsync(
            $"/api/ai-services/{configId}",
            ApiClientJson.ToJsonContent(new
            {
                name = $"updated-{Guid.NewGuid():N}",
                serviceType = 0,
                purpose = 1,
                priority = 0,
                endpoint = "https://api.openai.com/v1",
                apiKey = "",
                llmModel = "gpt-4.1-mini",
                disableThinking = false,
                defaultRecallTopK = 2
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(400);
        result.Message.Should().Contain("历史双用途");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = await dbContext.AiServiceConfigs.FindAsync(configId);
        entity.Should().NotBeNull();
        entity!.Purpose.Should().Be(AiServicePurpose.Llm | AiServicePurpose.Embedding);
        entity.LlmModel.Should().Be("gpt-4o-mini");
        entity.EmbeddingModel.Should().Be("text-embedding-3-small");
    }

    [Fact]
    public async Task AiServiceConfig_TestConnection_WithLegacyDualPurposeAndBothModels_ShouldRejectBeforeSelectingSingleSide()
    {
        var configId = await CreateLegacyCombinedPurposeConfigAsync(
            endpoint: "https://api.openai.com/v1",
            serviceType: AiServiceType.OpenAI,
            llmModel: "gpt-4o-mini",
            embeddingModel: "text-embedding-3-small");

        var response = await _client.PostAsync($"/api/ai-services/{configId}/test", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(400);
        result.Message.Should().Contain("历史双用途");
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
            CreatedAt = DateTime.UtcNow
        };

        dbContext.AiServiceConfigs.Add(entity);
        await dbContext.SaveChangesAsync();
        return entity.Id;
    }

    private async Task<int> CreateLegacyCombinedPurposeConfigAsync(
        string endpoint,
        AiServiceType serviceType,
        string? llmModel,
        string? embeddingModel)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = new AiServiceConfig
        {
            Name = $"legacy-purpose-{Guid.NewGuid():N}",
            ServiceType = serviceType,
            Purpose = AiServicePurpose.Llm | AiServicePurpose.Embedding,
            Priority = 0,
            Endpoint = endpoint,
            ApiKey = "legacy-purpose-key",
            LlmModel = llmModel,
            EmbeddingModel = embeddingModel,
            DisableThinking = false,
            CreatedAt = DateTime.UtcNow
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
