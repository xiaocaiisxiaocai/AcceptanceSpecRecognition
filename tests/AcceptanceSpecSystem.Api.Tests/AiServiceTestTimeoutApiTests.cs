using System.Diagnostics;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
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
        _client.Timeout = TimeSpan.FromSeconds(3);
    }

    [Fact]
    public async Task TestConnection_WhenLlmServiceHangs_ShouldReturnTimeoutResult()
    {
        var configId = await CreateOllamaConfigAsync();
        var stopwatch = Stopwatch.StartNew();

        using var response = await _client.PostAsync($"/api/ai-services/{configId}/test", null);

        stopwatch.Stop();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(0);
        result.Data.GetProperty("success").GetBoolean().Should().BeFalse();
        result.Data.GetProperty("message").GetString().Should().Contain("LLM: 测试超时");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    private async Task<int> CreateOllamaConfigAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = new AiServiceConfig
        {
            Name = $"timeout-ollama-{Guid.NewGuid():N}",
            ServiceType = AiServiceType.Ollama,
            Purpose = AiServicePurpose.Llm,
            Priority = 0,
            Endpoint = "http://127.0.0.1:11434/api",
            LlmModel = "qwen3.5:35b",
            DisableThinking = true,
            CreatedAt = DateTime.Now
        };

        dbContext.AiServiceConfigs.Add(entity);
        await dbContext.SaveChangesAsync();
        return entity.Id;
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
                ["AiServiceTest:TimeoutSeconds"] = "1"
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
        public IChatCompletionService CreateChatCompletionService(AiServiceConfig config)
            => new HangingChatCompletionService();

        public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiServiceConfig config)
            => throw new NotSupportedException("该测试未使用 Embedding。");
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
}
