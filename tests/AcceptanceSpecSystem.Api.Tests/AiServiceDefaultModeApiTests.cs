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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AcceptanceSpecSystem.Api.Tests;

public class AiServiceDefaultModeApiTests : IClassFixture<AiServiceDefaultModeApiWebApplicationFactory>
{
    private readonly AiServiceDefaultModeApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AiServiceDefaultModeApiTests(AiServiceDefaultModeApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions());
    }

    [Fact]
    public async Task TestConnection_WhenModeIsOmitted_ShouldUseFullMode()
    {
        var configId = await CreateConfigAsync(AiServicePurpose.Llm);

        using var response = await _client.PostAsync($"/api/ai-services/{configId}/test", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(0);
        result.Data.GetProperty("success").GetBoolean().Should().BeTrue();
        result.Data.GetProperty("message").GetString().Should().Be("LLM: OK");
    }

    private async Task<int> CreateConfigAsync(AiServicePurpose purpose)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = new AiServiceConfig
        {
            Name = $"default-mode-{Guid.NewGuid():N}",
            ServiceType = AiServiceType.OpenAI,
            Purpose = purpose,
            Priority = 0,
            Endpoint = "http://127.0.0.1:9",
            ApiKey = "test-key",
            LlmModel = purpose == AiServicePurpose.Llm ? "gpt-test" : null,
            EmbeddingModel = purpose == AiServicePurpose.Embedding ? "text-embedding-test" : null,
            DisableThinking = false,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.AiServiceConfigs.Add(entity);
        await dbContext.SaveChangesAsync();
        return entity.Id;
    }
}

public sealed class AiServiceDefaultModeApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(ISemanticKernelServiceFactory));
            services.AddSingleton<ISemanticKernelServiceFactory, SuccessSemanticKernelServiceFactory>();
        });
    }

    private sealed class SuccessSemanticKernelServiceFactory : ISemanticKernelServiceFactory
    {
        public IChatCompletionService CreateChatCompletionService(CoreAiServiceConfigModel config)
            => new SuccessChatCompletionService();

        public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(CoreAiServiceConfigModel config)
            => new SuccessEmbeddingGenerator();
    }

    private sealed class SuccessChatCompletionService : IChatCompletionService
    {
        public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ChatMessageContent> result =
            [
                new ChatMessageContent(AuthorRole.Assistant, "pong")
            ];
            return Task.FromResult(result);
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, "pong");
            await Task.CompletedTask;
        }
    }

    private sealed class SuccessEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var result = new GeneratedEmbeddings<Embedding<float>>(
            [
                new Embedding<float>(new[] { 0.1f, 0.2f, 0.3f })
            ]);
            return Task.FromResult(result);
        }
    }
}
