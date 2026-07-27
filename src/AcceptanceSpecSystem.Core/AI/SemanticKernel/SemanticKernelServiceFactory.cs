using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using AcceptanceSpecSystem.Core.AI.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenAI;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

public interface ISemanticKernelServiceFactory
{
    IChatCompletionService CreateChatCompletionService(AiServiceConfigModel config);

    IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiServiceConfigModel config);
}

public static class AiServiceHttpClientDefaults
{
    public const string OllamaNativeChatClientName = "OllamaNativeChatCompletionService";

    public static readonly TimeSpan LongRunningNetworkTimeout = TimeSpan.FromHours(12);
}

/// <summary>
/// Semantic Kernel 服务工厂（统一构建 LLM/Embedding 连接器）
/// 使用有界缓存复用实例，避免无限增长。
/// </summary>
public class SemanticKernelServiceFactory : ISemanticKernelServiceFactory, IDisposable
{
    /// <summary>
    /// AI 服务网络超时时间（秒）
    /// </summary>
    private const int CacheSizeLimit = 64;
    private static readonly TimeSpan CacheSlidingExpiration = TimeSpan.FromMinutes(30);

    private readonly Dictionary<string, ServiceCacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly object _cacheSync = new();
    private readonly ISafeAiHttpClientFactory _safeHttpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _azureOpenAiApiVersion;
    private long _cacheAccessSequence;
    private bool _disposed;

    public SemanticKernelServiceFactory(
        ILoggerFactory loggerFactory,
        ISafeAiHttpClientFactory safeHttpClientFactory,
        IOptions<SemanticKernelOptions> options)
    {
        _loggerFactory = loggerFactory;
        _safeHttpClientFactory = safeHttpClientFactory;
        _azureOpenAiApiVersion = string.IsNullOrWhiteSpace(options.Value.AzureOpenAIApiVersion)
            ? new SemanticKernelOptions().AzureOpenAIApiVersion
            : options.Value.AzureOpenAIApiVersion.Trim();
    }

    public IChatCompletionService CreateChatCompletionService(AiServiceConfigModel config)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(config.LlmModel))
            throw new InvalidOperationException("LLM 模型未配置");

        var key = BuildCacheKey(
            "chat",
            config.Id,
            config.ServiceType,
            config.Endpoint,
            config.LlmModel,
            config.ApiKey,
            config.DisableThinking,
            _safeHttpClientFactory.Generation);
        return GetOrCreateCached(key, () => CreateChatCompletionServiceInternal(config));
    }

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiServiceConfigModel config)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(config.EmbeddingModel))
            throw new InvalidOperationException("Embedding 模型未配置");

        var key = BuildCacheKey(
            "emb",
            config.Id,
            config.ServiceType,
            config.Endpoint,
            config.EmbeddingModel,
            config.ApiKey,
            config.DisableThinking,
            _safeHttpClientFactory.Generation);
        return GetOrCreateCached(key, () => CreateEmbeddingGeneratorInternal(config));
    }

    public void Dispose()
    {
        object[] values;
        lock (_cacheSync)
        {
            if (_disposed)
                return;

            _disposed = true;
            values = _cache.Values.Select(static entry => entry.Value).ToArray();
            _cache.Clear();
        }

        foreach (var value in values)
            DisposeCachedValue(value);
        GC.SuppressFinalize(this);
    }

    private T GetOrCreateCached<T>(string key, Func<T> factory) where T : class
    {
        List<object>? retired = null;
        T result;
        try
        {
            lock (_cacheSync)
            {
                ThrowIfDisposed();
                var now = DateTimeOffset.UtcNow;
                foreach (var expiredKey in _cache
                             .Where(pair => now - pair.Value.LastAccess >= CacheSlidingExpiration)
                             .Select(static pair => pair.Key)
                             .ToArray())
                {
                    retired ??= [];
                    retired.Add(_cache[expiredKey].Value);
                    _cache.Remove(expiredKey);
                }

                if (_cache.TryGetValue(key, out var cached))
                {
                    cached.Touch(++_cacheAccessSequence, now);
                    result = (T)cached.Value;
                }
                else
                {
                    result = factory();
                    _cache.Add(
                        key,
                        new ServiceCacheEntry(result, ++_cacheAccessSequence, now));

                    while (_cache.Count > CacheSizeLimit)
                    {
                        var oldest = _cache.MinBy(static pair => pair.Value.LastAccessSequence);
                        retired ??= [];
                        retired.Add(oldest.Value.Value);
                        _cache.Remove(oldest.Key);
                    }
                }
            }
        }
        finally
        {
            if (retired != null)
            {
                foreach (var value in retired)
                    DisposeCachedValue(value);
            }
        }

        return result;
    }

    private IChatCompletionService CreateChatCompletionServiceInternal(AiServiceConfigModel config)
    {
        var llmModel = RequireLlmModel(config);

        if (config.ServiceType == AiServiceType.Ollama)
        {
            var endpoint = NormalizeOllamaBaseUrl(RequireEndpoint(config));
            var ollamaClient = _safeHttpClientFactory.CreateClient(
                config.ServiceType,
                endpoint,
                AiServiceHttpClientDefaults.LongRunningNetworkTimeout);
            try
            {
                var logger = _loggerFactory.CreateLogger<OllamaNativeChatCompletionService>();
                return new OwnedChatCompletionService(
                    new OllamaNativeChatCompletionService(config, ollamaClient, logger),
                    ollamaClient);
            }
            catch
            {
                ollamaClient.Dispose();
                throw;
            }
        }

        var builder = Kernel.CreateBuilder();

        if (config.ServiceType == AiServiceType.AzureOpenAI)
        {
            var endpoint = RequireEndpoint(config);
            var httpClient = _safeHttpClientFactory.CreateClient(
                config.ServiceType,
                endpoint,
                AiServiceHttpClientDefaults.LongRunningNetworkTimeout);
            try
            {
                builder.AddAzureOpenAIChatCompletion(
                    deploymentName: llmModel,
                    endpoint: endpoint,
                    apiKey: config.ApiKey ?? string.Empty,
                    apiVersion: _azureOpenAiApiVersion,
                    httpClient: httpClient);
                var kernel = builder.Build();
                return new OwnedChatCompletionService(
                    kernel.GetRequiredService<IChatCompletionService>(),
                    httpClient);
            }
            catch
            {
                httpClient.Dispose();
                throw;
            }
        }

        var (client, httpClientOwner) = BuildOpenAIClient(config);
        try
        {
            builder.AddOpenAIChatCompletion(
                modelId: llmModel,
                openAIClient: client);
            var kernel = builder.Build();
            return new OwnedChatCompletionService(
                kernel.GetRequiredService<IChatCompletionService>(),
                httpClientOwner);
        }
        catch
        {
            httpClientOwner.Dispose();
            throw;
        }
    }

    private IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGeneratorInternal(AiServiceConfigModel config)
    {
        var embeddingModel = RequireEmbeddingModel(config);
        var builder = Kernel.CreateBuilder();

        if (config.ServiceType == AiServiceType.AzureOpenAI)
        {
            var endpoint = RequireEndpoint(config);
            var httpClient = _safeHttpClientFactory.CreateClient(
                config.ServiceType,
                endpoint,
                AiServiceHttpClientDefaults.LongRunningNetworkTimeout);
            try
            {
#pragma warning disable SKEXP0010
                builder.AddAzureOpenAIEmbeddingGenerator(
                    deploymentName: embeddingModel,
                    endpoint: endpoint,
                    apiKey: config.ApiKey ?? string.Empty,
                    apiVersion: _azureOpenAiApiVersion,
                    httpClient: httpClient);
#pragma warning restore SKEXP0010
                var kernel = builder.Build();
                return new OwnedEmbeddingGenerator(
                    kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
                    httpClient);
            }
            catch
            {
                httpClient.Dispose();
                throw;
            }
        }

        var (client, httpClientOwner) = BuildOpenAIClient(config);
        try
        {
#pragma warning disable SKEXP0010
            builder.AddOpenAIEmbeddingGenerator(
                modelId: embeddingModel,
                openAIClient: client);
#pragma warning restore SKEXP0010
            var kernel = builder.Build();
            return new OwnedEmbeddingGenerator(
                kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
                httpClientOwner);
        }
        catch
        {
            httpClientOwner.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 构建缓存 Key：配置变更（Endpoint/Model/ApiKey）自动创建新实例
    /// </summary>
    private static string BuildCacheKey(
        string prefix,
        int configId,
        AiServiceType serviceType,
        string? endpoint,
        string? model,
        string? apiKey,
        bool disableThinking,
        long policyGeneration)
    {
        return $"{prefix}_{configId}_{(int)serviceType}_{endpoint ?? ""}_{model ?? ""}_{apiKey?.GetHashCode() ?? 0}_{disableThinking}_{policyGeneration}";
    }

    private static string RequireEndpoint(AiServiceConfigModel config)
    {
        return AiEndpointNormalizer.NormalizeRequiredEndpoint(config.Endpoint);
    }

    private static string RequireLlmModel(AiServiceConfigModel config)
    {
        if (string.IsNullOrWhiteSpace(config.LlmModel))
        {
            throw new InvalidOperationException("LLM 模型未配置");
        }

        return config.LlmModel.Trim();
    }

    private static string RequireEmbeddingModel(AiServiceConfigModel config)
    {
        if (string.IsNullOrWhiteSpace(config.EmbeddingModel))
        {
            throw new InvalidOperationException("Embedding 模型未配置");
        }

        return config.EmbeddingModel.Trim();
    }

    /// <summary>
    /// 构建 OpenAIClient（用于 OpenAI 兼容服务：硅基流动、Ollama、LM Studio 等）
    /// 通过 OpenAIClientOptions 统一管理 Endpoint 和超时，无需手动创建 HttpClient
    /// </summary>
    private (OpenAIClient Client, HttpClient HttpClientOwner) BuildOpenAIClient(
        AiServiceConfigModel config)
    {
        var endpoint = BuildOpenAiEndpoint(config);
        var httpClient = _safeHttpClientFactory.CreateClient(
            config.ServiceType,
            endpoint,
            AiServiceHttpClientDefaults.LongRunningNetworkTimeout);
        try
        {
            var options = new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint),
                // OpenAI 兼容 SDK 需要保留网络超时配置，这里放宽到长时间推理可接受的级别。
                NetworkTimeout = AiServiceHttpClientDefaults.LongRunningNetworkTimeout,
                Transport = new HttpClientPipelineTransport(httpClient)
            };
            var credential = new ApiKeyCredential(config.ApiKey ?? "sk-placeholder");
            return (new OpenAIClient(credential, options), httpClient);
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    private static string BuildOpenAiEndpoint(AiServiceConfigModel config)
    {
        if (string.IsNullOrWhiteSpace(config.Endpoint))
            return "https://api.openai.com/v1";

        var value = AiEndpointNormalizer.NormalizeRequiredEndpoint(
            config.Endpoint,
            allowPrivateNetwork: config.ServiceType == AiServiceType.Ollama || config.ServiceType == AiServiceType.LMStudio).TrimEnd('/');
        if (config.ServiceType == AiServiceType.Ollama)
        {
            value = NormalizeOllamaBaseUrl(value);
        }

        if (value.EndsWith("/v1/v1", StringComparison.OrdinalIgnoreCase))
            value = value[..^3];
        if (!value.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            value += "/v1";
        return value;
    }

    private static string NormalizeOllamaBaseUrl(string endpoint)
    {
        var value = AiEndpointNormalizer.NormalizeRequiredEndpoint(
            endpoint,
            allowPrivateNetwork: true).TrimEnd('/');

        if (value.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        if (value.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^3];
        }

        return value.TrimEnd('/');
    }

    private static void DisposeCachedValue(object? value)
    {
        if (value is IDisposable disposable)
            disposable.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class ServiceCacheEntry(
        object value,
        long lastAccessSequence,
        DateTimeOffset lastAccess)
    {
        public object Value { get; } = value;
        public long LastAccessSequence { get; private set; } = lastAccessSequence;
        public DateTimeOffset LastAccess { get; private set; } = lastAccess;

        public void Touch(long sequence, DateTimeOffset timestamp)
        {
            LastAccessSequence = sequence;
            LastAccess = timestamp;
        }
    }

    internal sealed class OwnedChatCompletionService(
        IChatCompletionService inner,
        HttpClient httpClient) : IChatCompletionService, IDisposable
    {
        private readonly RetirableServiceOwner _owner = new(httpClient);

        public IReadOnlyDictionary<string, object?> Attributes => inner.Attributes;

        public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            using var lease = _owner.Acquire();
            return await inner.GetChatMessageContentsAsync(
                chatHistory,
                executionSettings,
                kernel,
                cancellationToken).ConfigureAwait(false);
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var lease = _owner.Acquire();
            await foreach (var item in inner.GetStreamingChatMessageContentsAsync(
                               chatHistory,
                               executionSettings,
                               kernel,
                               cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }

        public void Dispose() => _owner.Retire();
    }

    internal sealed class OwnedEmbeddingGenerator(
        IEmbeddingGenerator<string, Embedding<float>> inner,
        HttpClient httpClient)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly RetirableServiceOwner _owner = new(httpClient);

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this)
                ? this
                : inner.GetService(serviceType, serviceKey);

        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var lease = _owner.Acquire();
            return await inner.GenerateAsync(values, options, cancellationToken)
                .ConfigureAwait(false);
        }

        public void Dispose() => _owner.Retire();
    }

    private sealed class RetirableServiceOwner(HttpClient httpClient)
    {
        private readonly object _gate = new();
        private int _activeCalls;
        private bool _retired;
        private bool _disposed;

        public IDisposable Acquire()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_retired || _disposed, this);
                _activeCalls++;
                return new CallLease(this);
            }
        }

        public void Retire()
        {
            HttpClient? dispose = null;
            lock (_gate)
            {
                if (_retired)
                    return;

                _retired = true;
                if (_activeCalls == 0 && !_disposed)
                {
                    _disposed = true;
                    dispose = httpClient;
                }
            }

            dispose?.Dispose();
        }

        private void Release()
        {
            HttpClient? dispose = null;
            lock (_gate)
            {
                if (_activeCalls > 0)
                    _activeCalls--;
                if (_retired && _activeCalls == 0 && !_disposed)
                {
                    _disposed = true;
                    dispose = httpClient;
                }
            }

            dispose?.Dispose();
        }

        private sealed class CallLease(RetirableServiceOwner owner) : IDisposable
        {
            private RetirableServiceOwner? _owner = owner;

            public void Dispose() =>
                Interlocked.Exchange(ref _owner, null)?.Release();
        }
    }
}
