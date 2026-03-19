namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 审计日志实体（覆盖后端请求与前端上报事件）
/// </summary>
public class AuditLog
{
    /// <summary>
    /// 日志ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 日志来源
    /// </summary>
    public AuditLogSource Source { get; set; } = AuditLogSource.BackendRequest;

    /// <summary>
    /// 日志级别
    /// </summary>
    public AuditLogLevel Level { get; set; } = AuditLogLevel.Information;

    /// <summary>
    /// 事件类型（如 http.request、route.change）
    /// </summary>
    public string EventType { get; set; } = "http.request";

    /// <summary>
    /// 操作用户名（未登录可为空）
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// HTTP 请求方法
    /// </summary>
    public string? RequestMethod { get; set; }

    /// <summary>
    /// HTTP 请求路径
    /// </summary>
    public string? RequestPath { get; set; }

    /// <summary>
    /// 查询字符串
    /// </summary>
    public string? QueryString { get; set; }

    /// <summary>
    /// HTTP 状态码
    /// </summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// 请求耗时（毫秒）
    /// </summary>
    public long? DurationMs { get; set; }

    /// <summary>
    /// 客户端 IP
    /// </summary>
    public string? ClientIp { get; set; }

    /// <summary>
    /// 客户端 User-Agent
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// 客户端链路ID（前端注入）
    /// </summary>
    public string? ClientTraceId { get; set; }

    /// <summary>
    /// 前端当前路由（前端注入）
    /// </summary>
    public string? FrontendRoute { get; set; }

    /// <summary>
    /// 前端客户端ID（前端注入）
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// 扩展详情（JSON 文本）
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
