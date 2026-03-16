using System.ClientModel;
using System.Collections.Concurrent;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenAI;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

public interface ISemanticKernelServiceFactory
{
    IChatCompletionService CreateChatCompletionService(AiServiceConfig config);

    IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiServiceConfig config);
}

/// <summary>
/// Semantic Kernel 服务工厂（统一构建 LLM/Embedding 连接器）
/// 使用 OpenAIClient 管理连接，缓存实例避免重复创建 HTTP 连接
/// </summary>
public class SemanticKernelServiceFactory : ISemanticKernelServiceFactory
{
    private const string DefaultAzureApiVersion = "2024-02-15-preview";

    /// <summary>
    /// AI 服务网络超时时间（秒）
    /// </summary>
    private const int NetworkTimeoutSeconds = 120;

    // 缓存 Embedding/Chat 实例：key = "configId_endpoint_model_apiKey" 的哈希
    private static readonly ConcurrentDictionary<string, IEmbeddingGenerator<string, Embedding<float>>> _embeddingCache = new();
    private static readonly ConcurrentDictionary<string, IChatCompletionService> _chatCache = new();

    public IChatCompletionService CreateChatCompletionService(AiServiceConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.LlmModel))
            throw new InvalidOperationException("LLM 模型未配置");

        var key = BuildCacheKey("chat", config.Id, config.Endpoint, config.LlmModel, config.ApiKey);
        return _chatCache.GetOrAdd(key, _ => CreateChatCompletionServiceInternal(config));
    }

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiServiceConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.EmbeddingModel))
            throw new InvalidOperationException("Embedding 模型未配置");

        var key = BuildCacheKey("emb", config.Id, config.Endpoint, config.EmbeddingModel, config.ApiKey);
        return _embeddingCache.GetOrAdd(key, _ => CreateEmbeddingGeneratorInternal(config));
    }

    private static IChatCompletionService CreateChatCompletionServiceInternal(AiServiceConfig config)
    {
        var builder = Kernel.CreateBuilder();

        if (config.ServiceType == AiServiceType.AzureOpenAI)
        {
            var endpoint = RequireEndpoint(config);
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: config.LlmModel!,
                endpoint: endpoint,
                apiKey: config.ApiKey ?? string.Empty,
                apiVersion: DefaultAzureApiVersion);
        }
        else
        {
            var client = BuildOpenAIClient(config);
            builder.AddOpenAIChatCompletion(
                modelId: config.LlmModel!,
                openAIClient: client);
        }

        var kernel = builder.Build();
        return kernel.GetRequiredService<IChatCompletionService>();
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGeneratorInternal(AiServiceConfig config)
    {
        var builder = Kernel.CreateBuilder();

        if (config.ServiceType == AiServiceType.AzureOpenAI)
        {
            var endpoint = RequireEndpoint(config);
#pragma warning disable SKEXP0010
            builder.AddAzureOpenAIEmbeddingGenerator(
                deploymentName: config.EmbeddingModel!,
                endpoint: endpoint,
                apiKey: config.ApiKey ?? string.Empty,
                apiVersion: DefaultAzureApiVersion);
#pragma warning restore SKEXP0010
        }
        else
        {
            var client = BuildOpenAIClient(config);
#pragma warning disable SKEXP0010
            builder.AddOpenAIEmbeddingGenerator(
                modelId: config.EmbeddingModel!,
                openAIClient: client);
#pragma warning restore SKEXP0010
        }

        var kernel = builder.Build();
        return kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
    }

    /// <summary>
    /// 构建缓存 Key：配置变更（Endpoint/Model/ApiKey）自动创建新实例
    /// </summary>
    private static string BuildCacheKey(string prefix, int configId, string? endpoint, string? model, string? apiKey)
    {
        return $"{prefix}_{configId}_{endpoint ?? ""}_{model ?? ""}_{apiKey?.GetHashCode() ?? 0}";
    }

    private static string RequireEndpoint(AiServiceConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Endpoint))
            throw new InvalidOperationException("Endpoint 未配置");
        return config.Endpoint!.Trim();
    }

    /// <summary>
    /// 构建 OpenAIClient（用于 OpenAI 兼容服务：硅基流动、Ollama、LM Studio 等）
    /// 通过 OpenAIClientOptions 统一管理 Endpoint 和超时，无需手动创建 HttpClient
    /// </summary>
    private static OpenAIClient BuildOpenAIClient(AiServiceConfig config)
    {
        var endpoint = BuildOpenAiEndpoint(config.Endpoint);
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
            NetworkTimeout = TimeSpan.FromSeconds(NetworkTimeoutSeconds)
        };
        var credential = new ApiKeyCredential(config.ApiKey ?? "sk-placeholder");
        return new OpenAIClient(credential, options);
    }

    private static string BuildOpenAiEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return "https://api.openai.com/v1";

        var value = endpoint.Trim().TrimEnd('/');
        if (value.EndsWith("/v1/v1", StringComparison.OrdinalIgnoreCase))
            value = value[..^3];
        if (!value.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            value += "/v1";
        return value;
    }
}
