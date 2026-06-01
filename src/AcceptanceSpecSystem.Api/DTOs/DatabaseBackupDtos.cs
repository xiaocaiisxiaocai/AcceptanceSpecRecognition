using System.ComponentModel.DataAnnotations;

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
    [MaxLength(8, ErrorMessage = "备份执行时间不能超过8个字符")]
    public string? RunAtLocalTime { get; set; }
    [Required(ErrorMessage = "备份目录不能为空")]
    [MaxLength(500, ErrorMessage = "备份目录不能超过500个字符")]
    public string BackupDirectory { get; set; } = string.Empty;
    [Range(1, 365, ErrorMessage = "备份保留数量必须在 1 到 365 之间")]
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
