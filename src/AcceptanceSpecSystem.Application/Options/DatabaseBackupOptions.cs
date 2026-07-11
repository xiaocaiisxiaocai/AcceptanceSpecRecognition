namespace AcceptanceSpecSystem.Application.Options;

public sealed class DatabaseBackupOptions
{
    public const string SectionName = "DatabaseBackup";
    public bool Enabled { get; set; }
    public string? RunAtLocalTime { get; set; } = "02:00";
    public string BackupDirectory { get; set; } = "/app/backups";
    public int RetentionCount { get; set; } = 7;
}
