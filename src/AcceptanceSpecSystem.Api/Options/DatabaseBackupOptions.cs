namespace AcceptanceSpecSystem.Api.Options;

/// <summary>
/// 数据库备份配置选项。
/// </summary>
public class DatabaseBackupOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "DatabaseBackup";

    /// <summary>
    /// 是否启用自动备份。
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 每日本地执行时间。
    /// </summary>
    public string? RunAtLocalTime { get; set; } = "02:00";

    /// <summary>
    /// 备份文件目录。
    /// </summary>
    public string BackupDirectory { get; set; } = "/app/backups";

    /// <summary>
    /// 保留最近备份份数。
    /// </summary>
    public int RetentionCount { get; set; } = 7;
}
