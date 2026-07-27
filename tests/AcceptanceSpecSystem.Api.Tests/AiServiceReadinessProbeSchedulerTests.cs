using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Application.Options;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using FluentAssertions;
using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using CoreAiServiceConfig = AcceptanceSpecSystem.Core.AI.Models.AiServiceConfigModel;
using CoreAiServicePurpose = AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose;
using DataAiServicePurpose = AcceptanceSpecSystem.Data.Entities.AiServicePurpose;
using DataAiServiceType = AcceptanceSpecSystem.Data.Entities.AiServiceType;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class AiServiceReadinessProbeSchedulerTests
{
    [Fact]
    public async Task RequestProbe_WhenWorkersAndQueueAreFull_ShouldLeaveOverflowRetryable()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AiServiceReadinessOptions
        {
            MaxConcurrentProbes = 1,
            ProbeTimeoutSeconds = 30,
            StatusTtlSeconds = 60
        });
        var registry = new AiServiceReadinessRegistry(TimeProvider.System, options);
        var chat = new BlockingChatCompletionService();
        var lifetime = new TestHostApplicationLifetime();
        await using var scheduler = new AiServiceReadinessProbeScheduler(
            registry,
            new BlockingSemanticKernelServiceFactory(chat),
            new StubSafeAiHttpClientFactory(),
            lifetime,
            options,
            NullLogger<AiServiceReadinessProbeScheduler>.Instance);
        await scheduler.StartAsync(CancellationToken.None);

        RequestProbe(1);
        await chat.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var serviceId = 2; serviceId <= 20; serviceId++)
        {
            RequestProbe(serviceId);
        }

        Enumerable.Range(1, 20)
            .Count(serviceId => registry.GetSnapshot(serviceId, CoreAiServicePurpose.Llm).State ==
                                AiServiceReadinessState.Checking)
            .Should().Be(5, "一个工作线程加四个有界队列槽位只能接纳五个探测");
        Enumerable.Range(1, 20)
            .Count(serviceId => registry.GetSnapshot(serviceId, CoreAiServicePurpose.Llm).State ==
                                AiServiceReadinessState.Unknown)
            .Should().Be(15, "调度拥塞不是服务故障，溢出的探测必须保持可重试");
        registry.TryMarkChecking(20, CoreAiServicePurpose.Llm, out var retryGeneration)
            .Should().BeTrue();
        registry.ResetCheckingIfCurrent(20, CoreAiServicePurpose.Llm, retryGeneration)
            .Should().BeTrue();
        chat.MaxConcurrentCalls.Should().Be(1);

        await scheduler.StopAsync(CancellationToken.None);
        return;

        void RequestProbe(int serviceId)
        {
            registry.TryMarkChecking(serviceId, CoreAiServicePurpose.Llm, out var generation)
                .Should().BeTrue();
            scheduler.RequestProbe(CreateConfig(serviceId), CoreAiServicePurpose.Llm, generation);
        }
    }

    [Fact]
    public async Task StopAsync_RacingWithProbeRequests_ShouldCompleteAndClearCheckingStates()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AiServiceReadinessOptions
        {
            MaxConcurrentProbes = 1,
            ProbeTimeoutSeconds = 30,
            StatusTtlSeconds = 60
        });
        var registry = new AiServiceReadinessRegistry(TimeProvider.System, options);
        var lifetime = new TestHostApplicationLifetime();
        await using var scheduler = new AiServiceReadinessProbeScheduler(
            registry,
            new BlockingSemanticKernelServiceFactory(new BlockingChatCompletionService()),
            new StubSafeAiHttpClientFactory(),
            lifetime,
            options,
            NullLogger<AiServiceReadinessProbeScheduler>.Instance);
        await scheduler.StartAsync(CancellationToken.None);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestTasks = Enumerable.Range(1, 64)
            .Select(serviceId => Task.Run(async () =>
            {
                await start.Task;
                if (registry.TryMarkChecking(
                        serviceId,
                        CoreAiServicePurpose.Llm,
                        out var generation))
                {
                    scheduler.RequestProbe(
                        CreateConfig(serviceId),
                        CoreAiServicePurpose.Llm,
                        generation);
                }
            }))
            .ToArray();
        var stopTask = Task.Run(async () =>
        {
            await start.Task;
            await scheduler.StopAsync(CancellationToken.None);
        });

        start.TrySetResult();
        await Task.WhenAll(requestTasks.Append(stopTask)).WaitAsync(TimeSpan.FromSeconds(10));

        Enumerable.Range(1, 64)
            .Select(serviceId => registry.GetSnapshot(serviceId, CoreAiServicePurpose.Llm).State)
            .Should().OnlyContain(state => state != AiServiceReadinessState.Checking);
    }

    [Fact]
    public async Task RequestProbe_ForOllamaLlm_ShouldUseModelListWithoutStartingGeneration()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AiServiceReadinessOptions
        {
            MaxConcurrentProbes = 1,
            ProbeTimeoutSeconds = 2,
            StatusTtlSeconds = 60
        });
        var registry = new AiServiceReadinessRegistry(TimeProvider.System, options);
        var chat = new BlockingChatCompletionService();
        var lifetime = new TestHostApplicationLifetime();
        var safeClientFactory = new StubSafeAiHttpClientFactory(
            "{\"models\":[{\"name\":\"qwen2.5:14b\"}]}");
        await using var scheduler = new AiServiceReadinessProbeScheduler(
            registry,
            new BlockingSemanticKernelServiceFactory(chat),
            safeClientFactory,
            lifetime,
            options,
            NullLogger<AiServiceReadinessProbeScheduler>.Instance);
        await scheduler.StartAsync(CancellationToken.None);

        registry.TryMarkChecking(101, CoreAiServicePurpose.Llm, out var generation)
            .Should().BeTrue();
        scheduler.RequestProbe(CreateOllamaConfig(101), CoreAiServicePurpose.Llm, generation);

        await WaitForStateAsync(registry, 101, AiServiceReadinessState.Available);
        chat.Started.Task.IsCompleted.Should().BeFalse("轻量探测不应触发大模型冷启动生成");
        safeClientFactory.Calls.Should().ContainSingle();
        safeClientFactory.Calls[0].ServiceType.Should().Be(
            AcceptanceSpecSystem.Core.AI.Models.AiServiceType.Ollama);
        safeClientFactory.Calls[0].Endpoint.Should().Be("http://192.168.1.20:11434");
    }

    [Fact]
    public async Task RequestProbe_ForOllamaLlmWithoutConfiguredModel_ShouldReportUnavailable()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AiServiceReadinessOptions
        {
            MaxConcurrentProbes = 1,
            ProbeTimeoutSeconds = 2,
            StatusTtlSeconds = 60
        });
        var registry = new AiServiceReadinessRegistry(TimeProvider.System, options);
        var lifetime = new TestHostApplicationLifetime();
        await using var scheduler = new AiServiceReadinessProbeScheduler(
            registry,
            new BlockingSemanticKernelServiceFactory(new BlockingChatCompletionService()),
            new StubSafeAiHttpClientFactory("{\"models\":[{\"name\":\"another-model\"}]}"),
            lifetime,
            options,
            NullLogger<AiServiceReadinessProbeScheduler>.Instance);
        await scheduler.StartAsync(CancellationToken.None);

        registry.TryMarkChecking(102, CoreAiServicePurpose.Llm, out var generation)
            .Should().BeTrue();
        scheduler.RequestProbe(CreateOllamaConfig(102), CoreAiServicePurpose.Llm, generation);

        await WaitForStateAsync(registry, 102, AiServiceReadinessState.Unavailable);
    }

    private static AiServiceProbeConfig CreateConfig(int serviceId) => new(
        serviceId,
        $"probe-{serviceId}",
        DataAiServiceType.OpenAI,
        DataAiServicePurpose.Llm,
        0,
        "secret",
        "https://example.invalid/v1",
        null,
        "test-model",
        false,
        false,
        DateTime.UtcNow,
        null);

    private static AiServiceProbeConfig CreateOllamaConfig(int serviceId) => new(
        serviceId,
        $"ollama-{serviceId}",
        DataAiServiceType.Ollama,
        DataAiServicePurpose.Llm,
        0,
        null,
        "http://192.168.1.20:11434/api",
        null,
        "qwen2.5:14b",
        true,
        false,
        DateTime.UtcNow,
        null);

    private static async Task WaitForStateAsync(
        AiServiceReadinessRegistry registry,
        int serviceId,
        AiServiceReadinessState expected)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeoutAt)
        {
            if (registry.GetSnapshot(serviceId, CoreAiServicePurpose.Llm).State == expected)
                return;
            await Task.Delay(20);
        }

        registry.GetSnapshot(serviceId, CoreAiServicePurpose.Llm).State.Should().Be(expected);
    }

    private sealed class StubSafeAiHttpClientFactory(string modelsJson = "{\"models\":[]}")
        : ISafeAiHttpClientFactory
    {
        public List<(AcceptanceSpecSystem.Core.AI.Models.AiServiceType ServiceType, string Endpoint)> Calls { get; } = [];

        public HttpClient CreateClient(
            AcceptanceSpecSystem.Core.AI.Models.AiServiceType serviceType,
            string endpoint,
            TimeSpan? timeout = null)
        {
            Calls.Add((serviceType, endpoint));
            return new HttpClient(new StubHandler(modelsJson));
        }

        private sealed class StubHandler(string modelsJson) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                request.RequestUri!.AbsolutePath.Should().Be("/api/tags");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(modelsJson, Encoding.UTF8, "application/json")
                });
            }
        }
    }

    private sealed class BlockingSemanticKernelServiceFactory(BlockingChatCompletionService chat)
        : ISemanticKernelServiceFactory
    {
        public IChatCompletionService CreateChatCompletionService(CoreAiServiceConfig config) => chat;

        public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(CoreAiServiceConfig config) =>
            throw new NotSupportedException();
    }

    private sealed class BlockingChatCompletionService : IChatCompletionService
    {
        private int _activeCalls;
        private int _maxConcurrentCalls;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaxConcurrentCalls => Volatile.Read(ref _maxConcurrentCalls);
        public IReadOnlyDictionary<string, object?> Attributes { get; } =
            new Dictionary<string, object?>();

        public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _activeCalls);
            UpdateMaximum(active);
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return [];
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
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

        private void UpdateMaximum(int active)
        {
            var observed = Volatile.Read(ref _maxConcurrentCalls);
            while (active > observed)
            {
                var previous = Interlocked.CompareExchange(ref _maxConcurrentCalls, active, observed);
                if (previous == observed)
                    return;
                observed = previous;
            }
        }
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;
        public void StopApplication() => _stopping.Cancel();
    }
}
