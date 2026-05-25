namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 数据库备份页面配置与最近执行状态。
/// </summary>
public class DatabaseBackupSetting
{
    public int Id { get; set; }
    public bool Enabled { get; set; }
    public string? RunAtLocalTime { get; set; }
    public string BackupDirectory { get; set; } = string.Empty;
    public int RetentionCount { get; set; }
    public DateTime? LastStartedAt { get; set; }
    public DateTime? LastFinishedAt { get; set; }
    public bool? LastSucceeded { get; set; }
    public string? LastError { get; set; }
    public string? LastFileName { get; set; }
    public long? LastFileSizeBytes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
