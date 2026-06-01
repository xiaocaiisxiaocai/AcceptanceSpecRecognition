using System.ComponentModel.DataAnnotations;

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
    [MaxLength(100, ErrorMessage = "显示名称不能超过100个字符")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "内容不能为空")]
    [MaxLength(10000, ErrorMessage = "内容不能超过10000个字符")]
    public string Content { get; set; } = string.Empty;
}

public class PreviewPromptTemplateRequest
{
    [Required(ErrorMessage = "场景不能为空")]
    [MaxLength(100, ErrorMessage = "场景不能超过100个字符")]
    public string Scene { get; set; } = string.Empty;

    [Required(ErrorMessage = "内容不能为空")]
    [MaxLength(10000, ErrorMessage = "内容不能超过10000个字符")]
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

