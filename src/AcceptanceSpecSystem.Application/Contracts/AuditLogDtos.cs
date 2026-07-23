using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Contracts;

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

public class AuditLogDetailDto : AuditLogListItemDto
{
    public string? Details { get; set; }
}

public sealed record AuditTrailWriteCommand(
    AuditLogSource Source,
    AuditLogLevel Level,
    string EventType,
    string? Username,
    string? RequestMethod,
    string? RequestPath,
    string? QueryString,
    int? StatusCode,
    long? DurationMs,
    string? ClientIp,
    string? UserAgent,
    string? ClientTraceId,
    string? ClientId,
    string? FrontendRoute,
    string? Details,
    DateTime CreatedAt);
