using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.DTOs;

public class AiServiceConfigDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AiServiceType ServiceType { get; set; }
    public AiServicePurpose Purpose { get; set; }
    public int Priority { get; set; }
    public string? Endpoint { get; set; }
    public string? EmbeddingModel { get; set; }
    public string? LlmModel { get; set; }
    public bool HasApiKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AiServiceConfigDetailDto : AiServiceConfigDto
{
    public string? ApiKey { get; set; }
}

public class CreateAiServiceRequest
{
    public string Name { get; set; } = string.Empty;
    public AiServiceType ServiceType { get; set; }
    public AiServicePurpose Purpose { get; set; } = AiServicePurpose.Llm;
    public int Priority { get; set; } = 0;
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
    public string? EmbeddingModel { get; set; }
    public string? LlmModel { get; set; }
}

public class UpdateAiServiceRequest
{
    public string Name { get; set; } = string.Empty;
    public AiServiceType ServiceType { get; set; }
    public AiServicePurpose Purpose { get; set; } = AiServicePurpose.Llm;
    public int Priority { get; set; } = 0;
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
    public string? EmbeddingModel { get; set; }
    public string? LlmModel { get; set; }
}

public class AiServiceTestResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? HttpStatusCode { get; set; }
    public long ElapsedMs { get; set; }
}

public class AiServiceModelsResultDto
{
    public List<string> LlmModels { get; set; } = [];
    public List<string> EmbeddingModels { get; set; } = [];
    public string? Message { get; set; }
}

