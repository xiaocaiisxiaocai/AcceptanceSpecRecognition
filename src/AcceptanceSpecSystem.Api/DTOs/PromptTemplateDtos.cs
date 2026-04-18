namespace AcceptanceSpecSystem.Api.DTOs;

public class PromptTemplateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Scene { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public string UsageDescription { get; set; } = string.Empty;
    public List<string> AvailableVariables { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class UpdatePromptTemplateRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class PreviewPromptTemplateRequest
{
    public string Scene { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class PreviewPromptTemplateResponse
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = [];
    public string RenderedPrompt { get; set; } = string.Empty;
    public string? ExampleJson { get; set; }
    public bool StructuredOutputIsValid { get; set; }
    public string? StructuredOutputError { get; set; }
}

