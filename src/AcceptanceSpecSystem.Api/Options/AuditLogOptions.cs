namespace AcceptanceSpecSystem.Api.Options;

/// <summary>
/// 审计日志配置
/// </summary>
public class AuditLogOptions
{
    public const string SectionName = "AuditLog";

    /// <summary>
    /// 是否启用自动清理
    /// </summary>
    public bool EnableAutoCleanup { get; set; } = true;

    /// <summary>
    /// 保留天数（默认30天）
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// 清理间隔（小时）
    /// </summary>
    public int CleanupIntervalHours { get; set; } = 24;
}
