using System.ClientModel;
using AcceptanceSpecSystem.Core.AI.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
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
    private const int CacheSizeLimit = 128;
    private static readonly TimeSpan CacheSlidingExpiration = TimeSpan.FromMinutes(30);

    private readonly MemoryCache _cache;
    private readonly object _cacheSync = new();
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _azureOpenAiApiVersion;
    private bool _disposed;

    public SemanticKernelServiceFactory(
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory,
        IOptions<SemanticKernelOptions> options)
    {
        _loggerFactory = loggerFactory;
        _httpClientFactory = httpClientFactory;
        _azureOpenAiApiVersion = string.IsNullOrWhiteSpace(options.Value.AzureOpenAIApiVersion)
            ? new SemanticKernelOptions().AzureOpenAIApiVersion
            : options.Value.AzureOpenAIApiVersion.Trim();
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = CacheSizeLimit
        });
    }

    public IChatCompletionService CreateChatCompletionService(AiServiceConfigModel config)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(config.LlmModel))
            throw new InvalidOperationException("LLM 模型未配置");

        var key = BuildCacheKey("chat", config.Id, config.ServiceType, config.Endpoint, config.LlmModel, config.ApiKey, config.DisableThinking);
        return GetOrCreateCached(key, () => CreateChatCompletionServiceInternal(config));
    }

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiServiceConfigModel config)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(config.EmbeddingModel))
            throw new InvalidOperationException("Embedding 模型未配置");

        var key = BuildCacheKey("emb", config.Id, config.ServiceType, config.Endpoint, config.EmbeddingModel, config.ApiKey, config.DisableThinking);
        return GetOrCreateCached(key, () => CreateEmbeddingGeneratorInternal(config));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _cache.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private T GetOrCreateCached<T>(string key, Func<T> factory) where T : class
    {
        lock (_cacheSync)
        {
            if (_cache.TryGetValue(key, out T? cached) && cached != null)
                return cached;

            var created = factory();
            using var entry = _cache.CreateEntry(key);
            entry.SetSize(1);
            entry.SetSlidingExpiration(CacheSlidingExpiration);
            entry.RegisterPostEvictionCallback(static (_, value, _, _) => _ = DisposeIfNeededAsync(value));
            entry.Value = created;
            return created;
        }
    }

    private IChatCompletionService CreateChatCompletionServiceInternal(AiServiceConfigModel config)
    {
        var llmModel = RequireLlmModel(config);

        if (config.ServiceType == AiServiceType.Ollama)
        {
            var client = CreateOllamaHttpClient();
            var logger = _loggerFactory.CreateLogger<OllamaNativeChatCompletionService>();
            return new OllamaNativeChatCompletionService(config, client, logger);
        }

        var builder = Kernel.CreateBuilder();

        if (config.ServiceType == AiServiceType.AzureOpenAI)
        {
            var endpoint = RequireEndpoint(config);
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: llmModel,
                endpoint: endpoint,
                apiKey: config.ApiKey ?? string.Empty,
                apiVersion: _azureOpenAiApiVersion);
        }
        else
        {
            var client = BuildOpenAIClient(config);
            builder.AddOpenAIChatCompletion(
                modelId: llmModel,
                openAIClient: client);
        }

        var kernel = builder.Build();
        return kernel.GetRequiredService<IChatCompletionService>();
    }

    private IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGeneratorInternal(AiServiceConfigModel config)
    {
        var embeddingModel = RequireEmbeddingModel(config);
        var builder = Kernel.CreateBuilder();

        if (config.ServiceType == AiServiceType.AzureOpenAI)
        {
            var endpoint = RequireEndpoint(config);
#pragma warning disable SKEXP0010
            builder.AddAzureOpenAIEmbeddingGenerator(
                deploymentName: embeddingModel,
                endpoint: endpoint,
                apiKey: config.ApiKey ?? string.Empty,
                apiVersion: _azureOpenAiApiVersion);
#pragma warning restore SKEXP0010
        }
        else
        {
            var client = BuildOpenAIClient(config);
#pragma warning disable SKEXP0010
            builder.AddOpenAIEmbeddingGenerator(
                modelId: embeddingModel,
                openAIClient: client);
#pragma warning restore SKEXP0010
        }

        var kernel = builder.Build();
        return kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
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
        bool disableThinking)
    {
        return $"{prefix}_{configId}_{(int)serviceType}_{endpoint ?? ""}_{model ?? ""}_{apiKey?.GetHashCode() ?? 0}_{disableThinking}";
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
    private static OpenAIClient BuildOpenAIClient(AiServiceConfigModel config)
    {
        var endpoint = BuildOpenAiEndpoint(config);
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
            // OpenAI 兼容 SDK 需要保留网络超时配置，这里放宽到长时间推理可接受的级别。
            NetworkTimeout = AiServiceHttpClientDefaults.LongRunningNetworkTimeout
        };
        var credential = new ApiKeyCredential(config.ApiKey ?? "sk-placeholder");
        return new OpenAIClient(credential, options);
    }

    private HttpClient CreateOllamaHttpClient()
    {
        return _httpClientFactory.CreateClient(AiServiceHttpClientDefaults.OllamaNativeChatClientName);
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

    private static async Task DisposeIfNeededAsync(object? value)
    {
        if (value is IDisposable disposable)
        {
            disposable.Dispose();
        }
        else if (value is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
