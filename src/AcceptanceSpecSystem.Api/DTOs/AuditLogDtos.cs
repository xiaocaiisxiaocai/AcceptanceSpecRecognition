using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.DTOs;

/// <summary>
/// 审计日志列表项响应模型
/// </summary>
public class AuditLogListItemDto
{
    public int Id { get; set; }
    public AuditLogSource Source { get; set; }
    public AuditLogLevel Level { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? RequestMethod { get; set; }
    public string? RequestPath { get; set; }
    public string? QueryString { get; set; }
    public int? StatusCode { get; set; }
    public long? DurationMs { get; set; }
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public string? ClientTraceId { get; set; }
    public string? ClientId { get; set; }
    public string? FrontendRoute { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 审计日志详情响应模型
/// </summary>
public class AuditLogDetailDto : AuditLogListItemDto
{
    public string? Details { get; set; }
}
