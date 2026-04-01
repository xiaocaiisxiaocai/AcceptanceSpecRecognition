using System.Net;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

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
    public async Task MatchingKnowledge_GetSaveClearAndRestoreDefaults_ShouldWork()
    {
        var getResp = await _client.GetAsync("/api/matching-knowledge");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cfg = await getResp.ReadAsAsync<ApiResponse<JsonElement>>();
        cfg.Code.Should().Be(0);
        cfg.Data.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        cfg.Data.GetProperty("entityAliases").ValueKind.Should().Be(JsonValueKind.Object);
        cfg.Data.GetProperty("unitAliases").ValueKind.Should().Be(JsonValueKind.Object);
        cfg.Data.GetProperty("fieldAliases").ValueKind.Should().Be(JsonValueKind.Object);

        var putResp = await _client.PutAsync(
            "/api/matching-knowledge",
            ApiClientJson.ToJsonContent(new
            {
                entityAliases = new Dictionary<string, string>
                {
                    ["Panasonic品牌"] = "松下"
                },
                unitAliases = new Dictionary<string, string>
                {
                    ["公分"] = "cm"
                },
                unitFactors = new Dictionary<string, decimal>
                {
                    ["cm"] = 10m
                },
                fieldAliases = new Dictionary<string, string>
                {
                    ["宽尺寸"] = "宽度"
                },
                conflictPairs = new[]
                {
                    new
                    {
                        left = "正转",
                        right = "反转"
                    }
                }
            }));
        putResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await putResp.ReadAsAsync<ApiResponse<JsonElement>>();
        saved.Code.Should().Be(0);
        saved.Data.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        saved.Data.GetProperty("entityAliases").GetProperty("Panasonic品牌").GetString().Should().Be("松下");
        saved.Data.GetProperty("entityAliases").TryGetProperty("panasonic", out _).Should().BeFalse();

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await dbContext.Set<MatchingKnowledgeConfig>().SingleAsync();
            var savedEntityAliases = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.EntityAliasesJson);
            savedEntityAliases.Should().NotBeNull();
            savedEntityAliases!.Should().ContainKey("Panasonic品牌");
            savedEntityAliases.Should().NotContainKey("panasonic");
        }

        var clearResp = await _client.PostAsync("/api/matching-knowledge/clear", null);
        clearResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var clear = await clearResp.ReadAsAsync<ApiResponse<JsonElement>>();
        clear.Code.Should().Be(0);
        clear.Data.GetProperty("entityAliases").TryGetProperty("Panasonic品牌", out _).Should().BeFalse();
        clear.Data.GetProperty("entityAliases").TryGetProperty("panasonic", out _).Should().BeFalse();
        clear.Data.GetProperty("unitAliases").EnumerateObject().Should().BeEmpty();

        var restoreResp = await _client.PostAsync("/api/matching-knowledge/restore-defaults", null);
        restoreResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var restore = await restoreResp.ReadAsAsync<ApiResponse<JsonElement>>();
        restore.Code.Should().Be(0);
        restore.Data.GetProperty("entityAliases").TryGetProperty("Panasonic品牌", out _).Should().BeFalse();
        restore.Data.GetProperty("entityAliases").GetProperty("panasonic").GetString().Should().Be("松下");
    }

    [Fact]
    public async Task MatchingKnowledgeDraftGenerate_SpecFilterSource_ShouldReturnSingleCategoryDraft_AndNotModifyConfig()
    {
        var fixture = await SeedHistoricalSpecsAsync();

        var beforeGet = await _client.GetAsync("/api/matching-knowledge");
        beforeGet.StatusCode.Should().Be(HttpStatusCode.OK);
        var beforeJson = await beforeGet.ReadAsAsync<ApiResponse<JsonElement>>();
        beforeJson.Code.Should().Be(0);

        var response = await _client.PostAsync(
            "/api/matching-knowledge/drafts/generate",
            ApiClientJson.ToJsonContent(new
            {
                category = "entityAliases",
                specFilter = new
                {
                    customerId = fixture.CustomerId,
                    processId = fixture.ProcessId,
                    machineModelId = fixture.MachineModelId,
                    keyword = "Panasonic",
                    importedFrom = fixture.ImportedFrom,
                    importedTo = fixture.ImportedTo
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(0);
        body.Data.GetProperty("category").GetString().Should().Be("entityAliases");
        body.Data.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
        body.Data.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);

        var first = body.Data.GetProperty("items").EnumerateArray().First();
        first.GetProperty("key").GetString().Should().NotBeNullOrWhiteSpace();
        first.GetProperty("value").GetString().Should().NotBeNullOrWhiteSpace();
        first.GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
        first.GetProperty("status").GetString().Should().BeOneOf("ready", "duplicate", "conflict");

        var afterGet = await _client.GetAsync("/api/matching-knowledge");
        afterGet.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterJson = await afterGet.ReadAsAsync<ApiResponse<JsonElement>>();
        afterJson.Code.Should().Be(0);
        afterJson.Data.ToString().Should().Be(beforeJson.Data.ToString());
    }

    [Fact]
    public async Task MatchingKnowledgeDraftGenerate_SpecFilterSource_ShouldReturnBadRequest_WhenNoSpecsMatched()
    {
        var response = await _client.PostAsync(
            "/api/matching-knowledge/drafts/generate",
            ApiClientJson.ToJsonContent(new
            {
                category = "entityAliases",
                specFilter = new
                {
                    keyword = "这是一条不会命中的关键词"
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(400);
        body.Message.Should().Contain("没有可用于生成的历史验规");
    }

    [Fact]
    public async Task MatchingKnowledgeDraftGenerate_SpecFilterSource_ShouldReturnBadRequest_WhenMatchedSpecsExceedLimit()
    {
        var fixture = await SeedHistoricalSpecsAsync(specCount: 201, projectPrefix: "超上限");

        var response = await _client.PostAsync(
            "/api/matching-knowledge/drafts/generate",
            ApiClientJson.ToJsonContent(new
            {
                category = "entityAliases",
                specFilter = new
                {
                    customerId = fixture.CustomerId,
                    processId = fixture.ProcessId,
                    machineModelId = fixture.MachineModelId,
                    keyword = fixture.ProjectPrefix
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(400);
        body.Message.Should().Contain("命中的历史验规过多");
    }

    private async Task<(int CustomerId, int ProcessId, int MachineModelId, DateTime ImportedFrom, DateTime ImportedTo, string ProjectPrefix)> SeedHistoricalSpecsAsync(
        int specCount = 1,
        string projectPrefix = "Panasonic")
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var customer = new Customer
        {
            Name = $"草稿客户-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        };
        var process = new Process
        {
            Name = $"草稿制程-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        };
        var machineModel = new MachineModel
        {
            Name = $"草稿机型-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        };
        var wordFile = new WordFile
        {
            FileName = $"draft-{Guid.NewGuid():N}.docx",
            FileHash = Guid.NewGuid().ToString("N"),
            FileContent = Array.Empty<byte>(),
            UploadedAt = DateTime.UtcNow
        };

        dbContext.Customers.Add(customer);
        dbContext.Processes.Add(process);
        dbContext.MachineModels.Add(machineModel);
        dbContext.WordFiles.Add(wordFile);
        await dbContext.SaveChangesAsync();

        var importedFrom = DateTime.UtcNow.AddDays(-2);
        var importedTo = DateTime.UtcNow.AddDays(-1);

        for (var index = 0; index < specCount; index++)
        {
            dbContext.AcceptanceSpecs.Add(new AcceptanceSpec
            {
                CustomerId = customer.Id,
                ProcessId = process.Id,
                MachineModelId = machineModel.Id,
                Project = $"{projectPrefix} 控制柜 {index + 1}",
                Specification = $"{projectPrefix} 品牌控制柜尺寸检测 {index + 1}",
                Acceptance = "ABB 组件可共存",
                Remark = "匹配知识草稿测试",
                WordFileId = wordFile.Id,
                OwnerOrgUnitId = 1,
                CreatedByUserId = 1,
                ImportedAt = importedFrom.AddHours(12).AddMinutes(index)
            });
        }
        await dbContext.SaveChangesAsync();

        return (customer.Id, process.Id, machineModel.Id, importedFrom, importedTo, projectPrefix);
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
    public async Task AiServiceConfig_MatchingDefaults_ShouldPersist()
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
                defaultMatchingStrategy = 2,
                defaultRecallTopK = 3
            }));

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.ReadAsAsync<ApiResponse<JsonElement>>();
        created.Code.Should().Be(0);
        created.Data.GetProperty("defaultMatchingStrategy").GetInt32().Should().Be(2);
        created.Data.GetProperty("defaultRecallTopK").GetInt32().Should().Be(3);

        var id = created.Data.GetProperty("id").GetInt32();
        var getResp = await _client.GetAsync($"/api/ai-services/{id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await getResp.ReadAsAsync<ApiResponse<JsonElement>>();
        detail.Code.Should().Be(0);
        detail.Data.GetProperty("defaultMatchingStrategy").GetInt32().Should().Be(2);
        detail.Data.GetProperty("defaultRecallTopK").GetInt32().Should().Be(3);
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

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
