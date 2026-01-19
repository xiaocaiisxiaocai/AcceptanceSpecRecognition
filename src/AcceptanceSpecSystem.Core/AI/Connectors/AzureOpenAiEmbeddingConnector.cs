using AcceptanceSpecSystem.Core.AI.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;

namespace AcceptanceSpecSystem.Core.AI.Connectors;

/// <summary>
/// Azure OpenAI Embeddings 连接器（Semantic Kernel）
/// 约定：
/// - Endpoint: https://{resource}.openai.azure.com/
/// - EmbeddingModel: 部署名 (deployment)
/// - ApiKey: 资源 key
/// </summary>
public class AzureOpenAiEmbeddingConnector : IAiEmbeddingConnector
{
    private readonly AiServiceConfig _config;

    public AzureOpenAiEmbeddingConnector(AiServiceConfig config)
    {
        _config = config;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.Endpoint))
            throw new InvalidOperationException("Endpoint 未配置");
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
            throw new InvalidOperationException("ApiKey 未配置");
        if (string.IsNullOrWhiteSpace(_config.EmbeddingModel))
            throw new InvalidOperationException("EmbeddingModel(部署名) 未配置");

        var endpoint = _config.Endpoint!.Trim();
        var deploymentName = _config.EmbeddingModel!.Trim();

        var svc = new AzureOpenAITextEmbeddingGenerationService(deploymentName, endpoint, _config.ApiKey!);
        var embeddings = await svc.GenerateEmbeddingsAsync([text], new Kernel(), cancellationToken);
        return embeddings[0].Span.ToArray();
    }
}

