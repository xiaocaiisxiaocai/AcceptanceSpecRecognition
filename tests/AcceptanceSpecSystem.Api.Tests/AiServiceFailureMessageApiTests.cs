using System.Net;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using CoreAiServiceConfigModel = AcceptanceSpecSystem.Core.AI.Models.AiServiceConfigModel;

namespace AcceptanceSpecSystem.Api.Tests;

public class AiServiceFailureMessageApiTests
{
    [Fact]
    public async Task TestConnection_WhenQuickModeProbeReturnsUnauthorized_ShouldReturnApiKeyHint()
    {
        using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions());

        var port = GetFreeTcpPort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var responseBytes = Encoding.UTF8.GetBytes("""
                {
                  "error": {
                    "message": "Invalid Authentication"
                  }
                }
                """);

            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes);
            context.Response.Close();
        });

        var configId = await CreateConfigAsync(factory, AiServicePurpose.Llm, $"http://127.0.0.1:{port}", AiServiceType.LMStudio);

        using var response = await client.PostAsync($"/api/ai-services/{configId}/test?mode=quick", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(0);
        result.Data.GetProperty("success").GetBoolean().Should().BeFalse();
        result.Data.GetProperty("message").GetString()
            .Should().Be("LLM: 快速测试: 远端接口鉴权失败，请检查 ApiKey 是否正确");

        await serverTask;
    }

    [Fact]
    public async Task TestConnection_WhenFullModeReturnsUnauthorized_ShouldReturnApiKeyHint()
    {
        using var factory = new AiServiceFailureMessageApiWebApplicationFactory(
            new HttpRequestException("unauthorized", null, HttpStatusCode.Unauthorized));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions());
        var configId = await CreateConfigAsync(factory, AiServicePurpose.Llm);

        using var response = await client.PostAsync($"/api/ai-services/{configId}/test?mode=full", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(0);
        result.Data.GetProperty("success").GetBoolean().Should().BeFalse();
        result.Data.GetProperty("message").GetString()
            .Should().Be("LLM: 远端接口鉴权失败，请检查 ApiKey 是否正确");
    }

    [Fact]
    public async Task TestConnection_WhenQuickModeProbeReturnsNotFound_ShouldReturnEndpointHint()
    {
        using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions());

        var port = GetFreeTcpPort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"not found\"}");

            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes);
            context.Response.Close();
        });

        var configId = await CreateConfigAsync(factory, AiServicePurpose.Llm, $"http://127.0.0.1:{port}", AiServiceType.LMStudio);

        using var response = await client.PostAsync($"/api/ai-services/{configId}/test?mode=quick", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(0);
        result.Data.GetProperty("success").GetBoolean().Should().BeFalse();
        result.Data.GetProperty("message").GetString()
            .Should().Be("LLM: 快速测试: 远端接口地址无效，请检查 Endpoint 是否正确");

        await serverTask;
    }

    [Fact]
    public async Task TestConnection_WhenFullModeReturnsTooManyRequests_ShouldReturnRateLimitHint()
    {
        using var factory = new AiServiceFailureMessageApiWebApplicationFactory(
            new HttpRequestException("rate limit", null, HttpStatusCode.TooManyRequests));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions());
        var configId = await CreateConfigAsync(factory, AiServicePurpose.Llm);

        using var response = await client.PostAsync($"/api/ai-services/{configId}/test?mode=full", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(0);
        result.Data.GetProperty("success").GetBoolean().Should().BeFalse();
        result.Data.GetProperty("message").GetString()
            .Should().Be("LLM: 远端接口限流或额度受限，请稍后重试");
    }

    private static async Task<int> CreateConfigAsync(
        ApiWebApplicationFactory factory,
        AiServicePurpose purpose,
        string endpoint = "https://api.example.com",
        AiServiceType serviceType = AiServiceType.OpenAI)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = new AiServiceConfig
        {
            Name = $"failure-message-{Guid.NewGuid():N}",
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

public sealed class AiServiceFailureMessageApiWebApplicationFactory : ApiWebApplicationFactory
{
    private readonly Exception _chatException;

    public AiServiceFailureMessageApiWebApplicationFactory(Exception chatException)
    {
        _chatException = chatException;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(ISemanticKernelServiceFactory));
            services.AddSingleton<ISemanticKernelServiceFactory>(
                new FailureSemanticKernelServiceFactory(_chatException));
        });
    }

    private sealed class FailureSemanticKernelServiceFactory : ISemanticKernelServiceFactory
    {
        private readonly Exception _chatException;

        public FailureSemanticKernelServiceFactory(Exception chatException)
        {
            _chatException = chatException;
        }

        public IChatCompletionService CreateChatCompletionService(CoreAiServiceConfigModel config)
            => new FailureChatCompletionService(_chatException);

        public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(CoreAiServiceConfigModel config)
            => new FailureEmbeddingGenerator();
    }

    private sealed class FailureChatCompletionService : IChatCompletionService
    {
        private readonly Exception _exception;

        public FailureChatCompletionService(Exception exception)
        {
            _exception = exception;
        }

        public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<IReadOnlyList<ChatMessageContent>>(_exception);
        }

        public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            return ThrowAsync(_exception);
        }

        private static async IAsyncEnumerable<StreamingChatMessageContent> ThrowAsync(Exception exception)
        {
            await Task.Yield();
            throw exception;
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class FailureEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
