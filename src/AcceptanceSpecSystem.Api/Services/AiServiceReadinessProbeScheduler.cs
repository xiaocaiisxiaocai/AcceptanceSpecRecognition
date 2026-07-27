using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Application.Options;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using CoreAiServiceConfig = AcceptanceSpecSystem.Core.AI.Models.AiServiceConfigModel;
using CoreAiServicePurpose = AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose;
using CoreAiServiceType = AcceptanceSpecSystem.Core.AI.Models.AiServiceType;
using DataAiServicePurpose = AcceptanceSpecSystem.Data.Entities.AiServicePurpose;
using DataAiServiceType = AcceptanceSpecSystem.Data.Entities.AiServiceType;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 触发式轻量探测器。同一服务/用途只允许一个探测，且全局并发有明确上限。
/// </summary>
public sealed class AiServiceReadinessProbeScheduler :
    IAiServiceReadinessProbeScheduler,
    IHostedService,
    IAsyncDisposable
{
    private readonly AiServiceReadinessRegistry _registry;
    private readonly ISemanticKernelServiceFactory _factory;
    private readonly ISafeAiHttpClientFactory _safeHttpClientFactory;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<AiServiceReadinessProbeScheduler> _logger;
    private readonly TimeSpan _probeTimeout;
    private readonly ConcurrentDictionary<ProbeKey, long> _running = new();
    private readonly Channel<ProbeRequest> _queue;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _lifecycleGate = new();
    private readonly int _workerCount;
    private Task[] _workers = [];
    private bool _started;
    private bool _stopped;

    public AiServiceReadinessProbeScheduler(
        AiServiceReadinessRegistry registry,
        ISemanticKernelServiceFactory factory,
        ISafeAiHttpClientFactory safeHttpClientFactory,
        IHostApplicationLifetime applicationLifetime,
        IOptions<AiServiceReadinessOptions> options,
        ILogger<AiServiceReadinessProbeScheduler> logger)
    {
        _registry = registry;
        _factory = factory;
        _safeHttpClientFactory = safeHttpClientFactory;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
        _workerCount = Math.Clamp(options.Value.MaxConcurrentProbes, 1, 16);
        var queueCapacity = Math.Clamp(_workerCount * 4, _workerCount, 64);
        _queue = Channel.CreateBounded<ProbeRequest>(new BoundedChannelOptions(queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = _workerCount == 1,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _probeTimeout = TimeSpan.FromSeconds(Math.Clamp(options.Value.ProbeTimeoutSeconds, 1, 60));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        EnsureWorkersStarted();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task[] workers;
        lock (_lifecycleGate)
        {
            if (_stopped)
                return;

            _stopped = true;
            _queue.Writer.TryComplete();
            _shutdown.Cancel();
            workers = _workers;
        }

        if (workers.Length == 0)
            return;

        try
        {
            await Task.WhenAll(workers).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        foreach (var running in _running.ToArray())
        {
            _registry.ResetCheckingIfCurrent(
                running.Key.ServiceId,
                running.Key.Purpose,
                running.Value);
            _running.TryRemove(running.Key, out _);
        }
    }

    public void RequestProbe(
        AiServiceProbeConfig config,
        CoreAiServicePurpose purpose,
        long generation)
    {
        EnsureWorkersStarted();
        var key = new ProbeKey(config.Id, purpose);
        if (!_running.TryAdd(key, generation))
            return;

        if (!_queue.Writer.TryWrite(new ProbeRequest(key, config, purpose, generation)))
        {
            _running.TryRemove(key, out _);
            _registry.ResetCheckingIfCurrent(config.Id, purpose, generation);
            if (!_applicationLifetime.ApplicationStopping.IsCancellationRequested && !_stopped)
            {
                _logger.LogWarning(
                    "AI readiness 探测队列已满: serviceId={ServiceId}, purpose={Purpose}",
                    config.Id,
                    purpose);
            }
        }
    }

    private void EnsureWorkersStarted()
    {
        lock (_lifecycleGate)
        {
            if (_started || _stopped)
                return;

            _started = true;
            _workers = Enumerable.Range(0, _workerCount)
                .Select(_ => Task.Run(WorkerAsync))
                .ToArray();
        }
    }

    private async Task WorkerAsync()
    {
        try
        {
            await foreach (var request in _queue.Reader.ReadAllAsync(_shutdown.Token))
            {
                await ProbeAndRecordAsync(request);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    private async Task ProbeAndRecordAsync(
        ProbeRequest request)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                _applicationLifetime.ApplicationStopping,
                _shutdown.Token);
            timeout.CancelAfter(_probeTimeout);

            var coreConfig = ToCoreModel(request.Config);
            if (request.Config.ServiceType == DataAiServiceType.Ollama)
            {
                await ProbeOllamaModelAsync(request.Config, request.Purpose, timeout.Token);
            }
            else if (request.Purpose == CoreAiServicePurpose.Llm)
            {
                var chat = _factory.CreateChatCompletionService(coreConfig);
                var history = new ChatHistory();
                history.AddUserMessage("ping");
                await chat.GetChatMessageContentAsync(history, cancellationToken: timeout.Token);
            }
            else
            {
                var embedding = _factory.CreateEmbeddingGenerator(coreConfig);
                _ = await embedding.GenerateVectorAsync("ping", cancellationToken: timeout.Token);
            }

            _registry.ReportAvailableIfCurrent(
                request.Config.Id,
                request.Purpose,
                request.Generation);
        }
        catch (OperationCanceledException) when (
            _applicationLifetime.ApplicationStopping.IsCancellationRequested ||
            _shutdown.IsCancellationRequested)
        {
            _registry.ResetCheckingIfCurrent(
                request.Config.Id,
                request.Purpose,
                request.Generation);
        }
        catch (Exception ex)
        {
            _registry.ReportUnavailableIfCurrent(
                request.Config.Id,
                request.Purpose,
                request.Generation);
            _logger.LogWarning(
                "AI readiness 探测失败: serviceId={ServiceId}, purpose={Purpose}, exceptionType={ExceptionType}",
                request.Config.Id,
                request.Purpose,
                ex.GetType().Name);
        }
        finally
        {
            _running.TryRemove(request.Key, out _);
        }
    }

    private async Task ProbeOllamaModelAsync(
        AiServiceProbeConfig config,
        CoreAiServicePurpose purpose,
        CancellationToken cancellationToken)
    {
        var endpoint = AiEndpointNormalizer.NormalizeRequiredEndpoint(
            config.Endpoint,
            allowPrivateNetwork: true).TrimEnd('/');
        if (endpoint.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            endpoint = endpoint[..^4];

        var configuredModel = purpose == CoreAiServicePurpose.Llm
            ? config.LlmModel
            : config.EmbeddingModel;
        if (string.IsNullOrWhiteSpace(configuredModel))
            throw new InvalidOperationException("AI 模型未配置");

        using var client = _safeHttpClientFactory.CreateClient(
            CoreAiServiceType.Ollama,
            endpoint,
            AiServiceHttpClientDefaults.LongRunningNetworkTimeout);
        using var response = await client.GetAsync($"{endpoint}/api/tags", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("models", out var models) ||
            models.ValueKind != JsonValueKind.Array ||
            !models.EnumerateArray().Any(model => IsConfiguredOllamaModel(model, configuredModel)))
        {
            throw new InvalidOperationException("Ollama 未返回已配置模型");
        }
    }

    private static bool IsConfiguredOllamaModel(JsonElement model, string configuredModel)
    {
        foreach (var propertyName in new[] { "name", "model" })
        {
            if (model.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                string.Equals(value.GetString(), configuredModel.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _shutdown.Dispose();
    }

    private static CoreAiServiceConfig ToCoreModel(AiServiceProbeConfig config) => new()
    {
        Id = config.Id,
        Name = config.Name,
        ServiceType = config.ServiceType switch
        {
            DataAiServiceType.OpenAI => CoreAiServiceType.OpenAI,
            DataAiServiceType.AzureOpenAI => CoreAiServiceType.AzureOpenAI,
            DataAiServiceType.Ollama => CoreAiServiceType.Ollama,
            DataAiServiceType.LMStudio => CoreAiServiceType.LMStudio,
            _ => CoreAiServiceType.CustomOpenAICompatible
        },
        Purpose = config.Purpose switch
        {
            DataAiServicePurpose.Llm => CoreAiServicePurpose.Llm,
            DataAiServicePurpose.Embedding => CoreAiServicePurpose.Embedding,
            DataAiServicePurpose.Llm | DataAiServicePurpose.Embedding
                => CoreAiServicePurpose.Llm | CoreAiServicePurpose.Embedding,
            _ => CoreAiServicePurpose.None
        },
        Priority = config.Priority,
        ApiKey = config.ApiKey,
        Endpoint = config.Endpoint,
        EmbeddingModel = config.EmbeddingModel,
        LlmModel = config.LlmModel,
        DisableThinking = config.DisableThinking,
        CreatedAt = config.CreatedAt,
        UpdatedAt = config.UpdatedAt
    };

    private sealed record ProbeKey(int ServiceId, CoreAiServicePurpose Purpose);

    private sealed record ProbeRequest(
        ProbeKey Key,
        AiServiceProbeConfig Config,
        CoreAiServicePurpose Purpose,
        long Generation);
}
