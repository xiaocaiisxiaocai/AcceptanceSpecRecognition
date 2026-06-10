using System.ComponentModel.DataAnnotations;

namespace AcceptanceSpecSystem.Api.DTOs;

public sealed class EmbeddingCacheWarmupOptionsDto
{
    public bool Enabled { get; set; }
    public bool RunOnStartup { get; set; }
    public string? RunAtLocalTime { get; set; }
    public int IntervalHours { get; set; }
    public int BatchSize { get; set; }
    public int MaxItemsPerRun { get; set; }
}

public sealed class UpdateEmbeddingCacheWarmupOptionsRequest
{
    public bool Enabled { get; set; }
    public bool RunOnStartup { get; set; }
    [MaxLength(8, ErrorMessage = "预热执行时间不能超过8个字符")]
    public string? RunAtLocalTime { get; set; }
    [Range(1, 8760, ErrorMessage = "预热间隔小时数必须在 1 到 8760 之间")]
    public int IntervalHours { get; set; }
    [Range(1, 5000, ErrorMessage = "预热批量大小必须在 1 到 5000 之间")]
    public int BatchSize { get; set; }
    [Range(1, 1000000, ErrorMessage = "单次预热数量必须在 1 到 1000000 之间")]
    public int MaxItemsPerRun { get; set; }
}

public sealed class EmbeddingCacheWarmupStatusDto
{
    public bool IsRunning { get; set; }
    public DateTime? LastStartedAt { get; set; }
    public DateTime? LastFinishedAt { get; set; }
    public bool? LastSucceeded { get; set; }
    public string? LastError { get; set; }
    public int? LastBatchSize { get; set; }
    public int? LastMaxItemsPerRun { get; set; }
}

public sealed class EmbeddingCacheWarmupLastResultDto
{
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public int BatchSize { get; set; }
    public int MaxItemsPerRun { get; set; }
}

public sealed class EmbeddingCacheWarmupOverviewDto
{
    public EmbeddingCacheWarmupOptionsDto Options { get; set; } = new();
    public EmbeddingCacheWarmupStatusDto Status { get; set; } = new();
    public EmbeddingCacheWarmupLastResultDto? LastResult { get; set; }
}
