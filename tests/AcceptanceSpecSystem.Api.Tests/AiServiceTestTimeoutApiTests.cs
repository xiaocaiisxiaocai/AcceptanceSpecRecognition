using System.Diagnostics;
using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using CoreAiServiceConfigModel = AcceptanceSpecSystem.Core.AI.Models.AiServiceConfigModel;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AcceptanceSpecSystem.Api.Tests;

public class AiServiceTestTimeoutApiTests : IClassFixture<AiServiceTimeoutApiWebApplicationFactory>
{
    private readonly AiServiceTimeoutApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AiServiceTestTimeoutApiTests(AiServiceTimeoutApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions());
        _client.Timeout = TimeSpan.FromSeconds(10);
    }

    [Fact]
    public async Task TestConnection_WhenLlmServiceHangs_ShouldReturnLlmSpecificTimeoutResult()
    {
        var configId = await CreateConfigAsync(AiServicePurpose.Llm);
        var stopwatch = Stopwatch.StartNew();

        using var response = await _client.PostAsync($"/api/ai-services/{configId}/test?mode=full", null);

        stopwatch.Stop();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(0);
        result.Data.GetProperty("success").GetBoolean().Should().BeFalse();
        result.Data.GetProperty("message").GetString().Should().Contain("LLM: 测试超时（2秒）");
        result.Data.GetProperty("targetModel").GetString().Should().Be("gpt-test");
        result.Data.GetProperty("hostPort").GetString().Should().NotBeNullOrWhiteSpace();

        var elapsedMs = result.Data.GetProperty("elapsedMs").GetInt64();
        elapsedMs.Should().BeGreaterThanOrEqualTo(1800);
        elapsedMs.Should().BeLessThan(4000);
        var serviceElapsedMs = result.Data.GetProperty("serviceElapsedMs").GetInt64();
        serviceElapsedMs.Should().BeGreaterThanOrEqualTo(1800);
        serviceElapsedMs.Should().BeLessThan(4000);

        // 端到端耗时允许存在测试宿主调度抖动，但不应接近新的客户端超时。
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task TestConnection_WhenEmbeddingServiceHangs_ShouldReturnEmbeddingSpecificTimeoutResult()
    {
        var configId = await CreateConfigAsync(AiServicePurpose.Embedding);
        var stopwatch = Stopwatch.StartNew();

        using var response = await _client.PostAsync($"/api/ai-services/{configId}/test?mode=full", null);

        stopwatch.Stop();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(0);
        result.Data.GetProperty("success").GetBoolean().Should().BeFalse();
        result.Data.GetProperty("message").GetString().Should().Contain("Embedding: 测试超时（1秒）");
        result.Data.GetProperty("targetModel").GetString().Should().Be("text-embedding-test");
        result.Data.GetProperty("hostPort").GetString().Should().NotBeNullOrWhiteSpace();

        var elapsedMs = result.Data.GetProperty("elapsedMs").GetInt64();
        elapsedMs.Should().BeGreaterThanOrEqualTo(900);
        elapsedMs.Should().BeLessThan(3000);
        var serviceElapsedMs = result.Data.GetProperty("serviceElapsedMs").GetInt64();
        serviceElapsedMs.Should().BeGreaterThanOrEqualTo(900);
        serviceElapsedMs.Should().BeLessThan(3000);

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task TestConnection_WhenQuickModeLlmUsesModelProbe_ShouldReturnQuickSuccess()
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
                  "data": [
                    { "id": "gpt-test" }
                  ]
                }
                """;
            var responseBytes = System.Text.Encoding.UTF8.GetBytes(responseJson);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes);
            context.Response.Close();
        });

        var configId = await CreateConfigAsync(AiServicePurpose.Llm, $"http://127.0.0.1:{port}", AiServiceType.LMStudio);
        using var response = await _client.PostAsync($"/api/ai-services/{configId}/test?mode=quick", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(0);
        result.Data.GetProperty("success").GetBoolean().Should().BeTrue();
        result.Data.GetProperty("message").GetString().Should().Contain("LLM: OK");
        result.Data.GetProperty("message").GetString().Should().Contain("快速测试");
        result.Data.GetProperty("targetModel").GetString().Should().Be("gpt-test");
        result.Data.GetProperty("serviceElapsedMs").GetInt64().Should().BeLessThan(2000);

        await serverTask;
    }

    [Fact]
    public async Task TestConnection_WhenQuickModeEmbeddingUsesModelProbe_ShouldReturnQuickSuccess()
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
                  "data": [
                    { "id": "text-embedding-test" }
                  ]
                }
                """;
            var responseBytes = System.Text.Encoding.UTF8.GetBytes(responseJson);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes);
            context.Response.Close();
        });

        var configId = await CreateConfigAsync(AiServicePurpose.Embedding, $"http://127.0.0.1:{port}", AiServiceType.LMStudio);
        using var response = await _client.PostAsync($"/api/ai-services/{configId}/test?mode=quick", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(0);
        result.Data.GetProperty("success").GetBoolean().Should().BeTrue();
        result.Data.GetProperty("message").GetString().Should().Contain("Embedding: OK");
        result.Data.GetProperty("message").GetString().Should().Contain("快速测试");
        result.Data.GetProperty("targetModel").GetString().Should().Be("text-embedding-test");
        result.Data.GetProperty("serviceElapsedMs").GetInt64().Should().BeLessThan(2000);

        await serverTask;
    }

    private async Task<int> CreateConfigAsync(
        AiServicePurpose purpose,
        string endpoint = "https://api.example.com",
        AiServiceType serviceType = AiServiceType.OpenAI)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = new AiServiceConfig
        {
            Name = $"timeout-ollama-{Guid.NewGuid():N}",
            ServiceType = serviceType,
            Purpose = purpose,
            Priority = 0,
            Endpoint = endpoint,
            ApiKey = "test-key",
            LlmModel = purpose == AiServicePurpose.Llm ? "gpt-test" : null,
            EmbeddingModel = purpose == AiServicePurpose.Embedding ? "text-embedding-test" : null,
            DisableThinking = true,
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

public sealed class AiServiceTimeoutApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiServiceTest:TimeoutSeconds"] = "1",
                ["AiServiceTest:LlmTimeoutSeconds"] = "2",
                ["AiServiceTest:EmbeddingTimeoutSeconds"] = "1"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(ISemanticKernelServiceFactory));
            services.AddSingleton<ISemanticKernelServiceFactory, HangingSemanticKernelServiceFactory>();
        });
    }

    private sealed class HangingSemanticKernelServiceFactory : ISemanticKernelServiceFactory
    {
        public IChatCompletionService CreateChatCompletionService(CoreAiServiceConfigModel config)
            => new HangingChatCompletionService();

        public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(CoreAiServiceConfigModel config)
            => new HangingEmbeddingGenerator();
    }

    private sealed class HangingChatCompletionService : IChatCompletionService
    {
        public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

        public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
    }

    private sealed class HangingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new GeneratedEmbeddings<Embedding<float>>([]);
        }
    }
}
