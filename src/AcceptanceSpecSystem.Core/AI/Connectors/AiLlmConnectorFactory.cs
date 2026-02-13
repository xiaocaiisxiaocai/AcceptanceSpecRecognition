using AcceptanceSpecSystem.Core.AI.Interfaces;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Core.AI.Connectors;

public class AiLlmConnectorFactory : IAiLlmConnectorFactory
{
    public IAiLlmConnector Create(AiServiceConfig config)
    {
        return config.ServiceType switch
        {
            AiServiceType.OpenAI or AiServiceType.LMStudio or AiServiceType.CustomOpenAICompatible
                => new OpenAiLlmConnector(config),
            AiServiceType.AzureOpenAI
                => new AzureOpenAiLlmConnector(config),
            AiServiceType.Ollama
                => new OllamaLlmConnector(config),
            _ => new OpenAiLlmConnector(config)
        };
    }
}
