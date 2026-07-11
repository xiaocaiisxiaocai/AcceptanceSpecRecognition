using System.ComponentModel.DataAnnotations;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Contracts;

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
    public bool DisableThinking { get; set; }
    public bool IsDisabled { get; set; }
    public int DefaultRecallTopK { get; set; } = 2;
    public bool HasApiKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AiServiceConfigDetailDto : AiServiceConfigDto { public string? ApiKey { get; set; } }

public class CreateAiServiceRequest
{
    [Required(ErrorMessage = "名称不能为空")]
    [MaxLength(100, ErrorMessage = "名称不能超过100个字符")]
    public string Name { get; set; } = string.Empty;
    public AiServiceType ServiceType { get; set; }
    public AiServicePurpose Purpose { get; set; } = AiServicePurpose.Llm;
    [Range(0, 9999, ErrorMessage = "优先级必须在 0 到 9999 之间")]
    public int Priority { get; set; }
    [MaxLength(4000, ErrorMessage = "ApiKey不能超过4000个字符")]
    public string? ApiKey { get; set; }
    [MaxLength(500, ErrorMessage = "Endpoint不能超过500个字符")]
    public string? Endpoint { get; set; }
    [MaxLength(200, ErrorMessage = "Embedding模型名称不能超过200个字符")]
    public string? EmbeddingModel { get; set; }
    [MaxLength(200, ErrorMessage = "LLM模型名称不能超过200个字符")]
    public string? LlmModel { get; set; }
    public bool DisableThinking { get; set; }
    [Range(1, 3, ErrorMessage = "默认召回数量必须在 1 到 3 之间")]
    public int DefaultRecallTopK { get; set; } = 2;
}

public class UpdateAiServiceRequest : CreateAiServiceRequest { }
public class SetAiServiceDisabledRequest { public bool IsDisabled { get; set; } }
public enum AiServiceConnectionTestMode { Quick = 0, Full = 1 }

public class AiServiceTestResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? HttpStatusCode { get; set; }
    public long ElapsedMs { get; set; }
    public long? ServiceElapsedMs { get; set; }
    public string? TargetModel { get; set; }
    public string? TargetEndpoint { get; set; }
    public string? HostPort { get; set; }
}

public class AiServiceModelsResultDto
{
    public List<string> LlmModels { get; set; } = [];
    public List<string> EmbeddingModels { get; set; } = [];
    public string? Message { get; set; }
}

/// <summary>供 API 外部服务探测适配使用的只读配置，不暴露持久化实体。</summary>
public sealed record AiServiceProbeConfig(
    int Id, string Name, AiServiceType ServiceType, AiServicePurpose Purpose, int Priority,
    string? ApiKey, string? Endpoint, string? EmbeddingModel, string? LlmModel,
    bool DisableThinking, bool IsDisabled, DateTime CreatedAt, DateTime? UpdatedAt);

