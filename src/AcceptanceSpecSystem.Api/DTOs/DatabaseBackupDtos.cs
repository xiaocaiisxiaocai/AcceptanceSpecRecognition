namespace AcceptanceSpecSystem.Api.DTOs;

public sealed class DatabaseBackupOptionsDto
{
    public bool Enabled { get; set; }
    public string? RunAtLocalTime { get; set; }
    public string BackupDirectory { get; set; } = string.Empty;
    public int RetentionCount { get; set; }
}

public sealed class UpdateDatabaseBackupOptionsRequest
{
    public bool Enabled { get; set; }
    public string? RunAtLocalTime { get; set; }
    public string BackupDirectory { get; set; } = string.Empty;
    public int RetentionCount { get; set; }
}

public sealed class DatabaseBackupStatusDto
{
    public bool IsRunning { get; set; }
    public DateTime? LastStartedAt { get; set; }
    public DateTime? LastFinishedAt { get; set; }
    public bool? LastSucceeded { get; set; }
    public string? LastError { get; set; }
    public string? LastFileName { get; set; }
    public long? LastFileSizeBytes { get; set; }
}

public sealed class DatabaseBackupFileDto
{
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class DatabaseBackupOverviewDto
{
    public DatabaseBackupOptionsDto Options { get; set; } = new();
    public DatabaseBackupStatusDto Status { get; set; } = new();
    public List<DatabaseBackupFileDto> Files { get; set; } = [];
}
