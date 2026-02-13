using AcceptanceSpecSystem.Data.Entities;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

public interface ISemanticKernelServiceFactory
{
    IChatCompletionService CreateChatCompletionService(AiServiceConfig config);

    IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiServiceConfig config);
}

/// <summary>
/// Semantic Kernel 服务工厂（统一构建 LLM/Embedding 连接器）
/// </summary>
public class SemanticKernelServiceFactory : ISemanticKernelServiceFactory
{
    private const string DefaultAzureApiVersion = "2024-02-15-preview";

    public IChatCompletionService CreateChatCompletionService(AiServiceConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.LlmModel))
            throw new InvalidOperationException("LLM 模型未配置");

        var builder = Kernel.CreateBuilder();

        if (config.ServiceType == AiServiceType.AzureOpenAI)
        {
            var endpoint = RequireEndpoint(config);
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: config.LlmModel,
                endpoint: endpoint,
                apiKey: config.ApiKey ?? string.Empty,
                apiVersion: DefaultAzureApiVersion);
        }
        else
        {
            var endpoint = BuildOpenAiEndpoint(config.Endpoint);
            var httpClient = BuildHttpClient(endpoint);
            builder.AddOpenAIChatCompletion(
                modelId: config.LlmModel,
                apiKey: config.ApiKey ?? string.Empty,
                httpClient: httpClient);
        }

        var kernel = builder.Build();
        return kernel.GetRequiredService<IChatCompletionService>();
    }

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiServiceConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.EmbeddingModel))
            throw new InvalidOperationException("Embedding 模型未配置");

        var builder = Kernel.CreateBuilder();

        if (config.ServiceType == AiServiceType.AzureOpenAI)
        {
            var endpoint = RequireEndpoint(config);
#pragma warning disable SKEXP0010
            builder.AddAzureOpenAIEmbeddingGenerator(
                deploymentName: config.EmbeddingModel,
                endpoint: endpoint,
                apiKey: config.ApiKey ?? string.Empty,
                apiVersion: DefaultAzureApiVersion);
#pragma warning restore SKEXP0010
        }
        else
        {
            var endpoint = BuildOpenAiEndpoint(config.Endpoint);
            var httpClient = BuildHttpClient(endpoint);
#pragma warning disable SKEXP0010
            builder.AddOpenAIEmbeddingGenerator(
                modelId: config.EmbeddingModel,
                apiKey: config.ApiKey ?? string.Empty,
                httpClient: httpClient);
#pragma warning restore SKEXP0010
        }

        var kernel = builder.Build();
        return kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
    }

    private static string RequireEndpoint(AiServiceConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Endpoint))
            throw new InvalidOperationException("Endpoint 未配置");
        return config.Endpoint!.Trim();
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

    private static HttpClient BuildHttpClient(string endpoint)
    {
        return new HttpClient
        {
            BaseAddress = new Uri(endpoint),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }
}
