namespace AcceptanceSpecSystem.Core.AI.Models;

public enum AiServiceType
{
    OpenAI,
    AzureOpenAI,
    Ollama,
    LMStudio,
    CustomOpenAICompatible
}

[Flags]
public enum AiServicePurpose
{
    None = 0,
    Llm = 1,
    Embedding = 2
}

public class AiServiceConfigModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public AiServiceType ServiceType { get; set; }

    public AiServicePurpose Purpose { get; set; } = AiServicePurpose.Llm;

    public int Priority { get; set; }

    public string? ApiKey { get; set; }

    public string? Endpoint { get; set; }

    public string? EmbeddingModel { get; set; }

    public string? LlmModel { get; set; }

    public bool DisableThinking { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
