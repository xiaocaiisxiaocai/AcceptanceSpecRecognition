using System.Collections.Concurrent;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AcceptanceSpecSystem.Api.Tests;

public class LlmMatchingAssistConcurrencyTests : IClassFixture<LlmAssistConcurrencyWebApplicationFactory>
{
    private readonly LlmAssistConcurrencyWebApplicationFactory _factory;

    public LlmMatchingAssistConcurrencyTests(LlmAssistConcurrencyWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ScopedLlmAssistServices_ShouldShareInstanceAndSerializeDbBackedCacheInitialization()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider
            .GetRequiredService<AiServiceReadinessRegistry>()
            .ReportAvailable(71, AiServicePurpose.Llm);
        var reviewService = scope.ServiceProvider.GetRequiredService<ILlmReviewService>();
        var adjudicationService = scope.ServiceProvider.GetRequiredService<ILlmEquivalenceAdjudicationService>();
        var rerankService = scope.ServiceProvider.GetRequiredService<ILlmCandidateRerankService>();

        reviewService.Should().BeSameAs(adjudicationService);
        reviewService.Should().BeSameAs(rerankService);

        var reviewTask = reviewService.ReviewAsync(new LlmReviewRequest
        {
            SourceProject = "项目",
            SourceSpecification = "源规格",
            BestMatchProject = "项目",
            BestMatchSpecification = "候选规格",
            CurrentDecision = "manualReview"
        });

        await _factory.DbGate.WaitUntilEnteredAsync();

        var adjudicationTask = adjudicationService.AdjudicateAsync(new LlmEquivalenceAdjudicationRequest
        {
            SourceProject = "项目",
            SourceSpecification = "源规格",
            CandidateProject = "项目",
            CandidateSpecification = "候选规格",
            CurrentDecision = "manualReview"
        });

        var act = async () => await Task.WhenAll(reviewTask, adjudicationTask);

        await act.Should().NotThrowAsync();
        _factory.PromptProvider.CallCount.Should().Be(2);
        _factory.ConfigProvider.CallCount.Should().Be(1);
    }
}

public sealed class LlmAssistConcurrencyWebApplicationFactory : ApiWebApplicationFactory
{
    internal NonConcurrentDbGate DbGate { get; } = new();
    internal GatePromptTemplateProvider PromptProvider { get; }
    internal GateAiServiceConfigProvider ConfigProvider { get; }

    public LlmAssistConcurrencyWebApplicationFactory()
    {
        PromptProvider = new GatePromptTemplateProvider(DbGate);
        ConfigProvider = new GateAiServiceConfigProvider(DbGate);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(ILlmReviewService));
            services.RemoveAll(typeof(ILlmEquivalenceAdjudicationService));
            services.RemoveAll(typeof(ILlmCandidateRerankService));
            services.RemoveAll(typeof(LlmMatchingAssistService));
            services.RemoveAll(typeof(IPromptTemplateProvider));
            services.RemoveAll(typeof(IAiServiceConfigProvider));
            services.RemoveAll(typeof(ISemanticKernelServiceFactory));

            services.AddSingleton<IPromptTemplateProvider>(PromptProvider);
            services.AddSingleton<IAiServiceConfigProvider>(ConfigProvider);
            services.AddScoped<IAiServiceSelector, AiServiceSelector>();
            services.AddSingleton<ISemanticKernelServiceFactory, GateSemanticKernelServiceFactory>();
            services.AddScoped<LlmMatchingAssistService>();
            services.AddScoped<ILlmReviewService>(sp => sp.GetRequiredService<LlmMatchingAssistService>());
            services.AddScoped<ILlmEquivalenceAdjudicationService>(sp => sp.GetRequiredService<LlmMatchingAssistService>());
            services.AddScoped<ILlmCandidateRerankService>(sp => sp.GetRequiredService<LlmMatchingAssistService>());
        });
    }
}

internal sealed class NonConcurrentDbGate
{
    private readonly TaskCompletionSource _entered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _activeCalls;

    public Task WaitUntilEnteredAsync()
    {
        return _entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public async Task<T> RunAsync<T>(string operation, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _activeCalls) > 1)
        {
            Interlocked.Decrement(ref _activeCalls);
            throw new InvalidOperationException($"{operation} 不允许与其他 DB 操作并发");
        }

        _entered.TrySetResult();
        try
        {
            await Task.Delay(120, cancellationToken);
            return await action();
        }
        finally
        {
            Interlocked.Decrement(ref _activeCalls);
        }
    }
}

internal sealed class GatePromptTemplateProvider : IPromptTemplateProvider
{
    private readonly NonConcurrentDbGate _gate;

    public GatePromptTemplateProvider(NonConcurrentDbGate gate)
    {
        _gate = gate;
    }

    public int CallCount { get; private set; }

    public Task<PromptTemplateModel> GetOrCreateSystemAsync(
        PromptTemplateScene scene,
        string name,
        string displayName,
        string defaultContent,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return _gate.RunAsync(
            "PromptTemplateProvider",
            () => Task.FromResult(new PromptTemplateModel
            {
                Id = (int)scene,
                Content = GetTemplateContent(scene)
            }),
            cancellationToken);
    }

    public Task SaveContentAsync(int id, string content, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    private static string GetTemplateContent(PromptTemplateScene scene)
    {
        return scene switch
        {
            PromptTemplateScene.MatchingReview =>
                "【业务场景】{{workflowScene}}\n源项目：{{sourceProject}}\n源规格：{{sourceSpecification}}\n系统匹配结果：{{bestMatchProject}}/{{bestMatchSpecification}}\n当前决策：{{currentDecision}}\n仅返回严格 JSON",
            PromptTemplateScene.MatchingEquivalenceAdjudication =>
                "源项目：{{sourceProject}}\n源规格：{{sourceSpecification}}\n候选项目：{{candidateProject}}\n候选规格：{{candidateSpecification}}\n当前决策：{{currentDecision}}\n仅返回严格 JSON",
            _ => "仅返回严格 JSON"
        };
    }
}

internal sealed class GateAiServiceConfigProvider : IAiServiceConfigProvider
{
    private readonly NonConcurrentDbGate _gate;
    private readonly IReadOnlyList<AiServiceConfigModel> _configs =
    [
        new()
        {
            Id = 71,
            Name = "llm-test",
            Purpose = AiServicePurpose.Llm,
            LlmModel = "gpt-test",
            Priority = 0,
            CreatedAt = DateTime.UtcNow
        }
    ];

    public GateAiServiceConfigProvider(NonConcurrentDbGate gate)
    {
        _gate = gate;
    }

    public int CallCount { get; private set; }

    public Task<IReadOnlyList<AiServiceConfigModel>> GetByPurposeAsync(
        AiServicePurpose purpose,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return _gate.RunAsync(
            "AiServiceConfigProvider",
            () => Task.FromResult(_configs),
            cancellationToken);
    }
}

internal sealed class GateSemanticKernelServiceFactory : ISemanticKernelServiceFactory
{
    public IChatCompletionService CreateChatCompletionService(AiServiceConfigModel config)
    {
        return new GateChatCompletionService();
    }

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiServiceConfigModel config)
    {
        throw new NotSupportedException();
    }
}

internal sealed class GateChatCompletionService : IChatCompletionService
{
    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = chatHistory.Last().Content ?? string.Empty;
        var content = prompt.Contains("系统匹配结果", StringComparison.Ordinal)
            ? """{"score":92,"reason":"复核通过","commentary":"已比较项目与规格"}"""
            : """{"verdict":"equivalent","reasonType":"equivalent_expression","reason":"项目与规格一致","confidence":0.95}""";
        IReadOnlyList<ChatMessageContent> result =
        [
            new ChatMessageContent(AuthorRole.Assistant, content)
        ];
        return Task.FromResult(result);
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new StreamingChatMessageContent(AuthorRole.Assistant, "{}");
        await Task.CompletedTask;
    }
}
