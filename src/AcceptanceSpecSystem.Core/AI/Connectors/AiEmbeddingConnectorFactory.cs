using AcceptanceSpecSystem.Core.AI.Interfaces;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Core.AI.Connectors;

public class AiEmbeddingConnectorFactory : IAiEmbeddingConnectorFactory
{
    public IAiEmbeddingConnector Create(AiServiceConfig config)
    {
        return config.ServiceType switch
        {
            AiServiceType.OpenAI or AiServiceType.LMStudio or AiServiceType.CustomOpenAICompatible
                => new OpenAiEmbeddingConnector(config),
            AiServiceType.AzureOpenAI
                => new AzureOpenAiEmbeddingConnector(config),
            AiServiceType.Ollama
                => new OllamaEmbeddingConnector(config),
            _ => new OpenAiEmbeddingConnector(config) // 尝试按 OpenAI-compatible 处理
        };
    }
}

