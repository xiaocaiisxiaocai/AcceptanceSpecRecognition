namespace AcceptanceSpecRecognition.Core.Models;

/// <summary>
/// 健康检查结果
/// </summary>
public class HealthCheckResult
{
    public DateTime Timestamp { get; set; }
    public bool IsHealthy { get; set; }
    public HealthStatus Status { get; set; }
    public List<ComponentHealth> Checks { get; set; } = new();
}

/// <summary>
/// 组件健康状态
/// </summary>
public class ComponentHealth
{
    public string Component { get; set; } = string.Empty;
    public HealthStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CheckedAt { get; set; }
}

/// <summary>
/// 健康状态枚举
/// </summary>
public enum HealthStatus
{
    Healthy,
    Degraded,
    Unhealthy
}
