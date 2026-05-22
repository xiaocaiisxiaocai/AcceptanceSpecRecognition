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
    public string? RunAtLocalTime { get; set; }
    public int IntervalHours { get; set; }
    public int BatchSize { get; set; }
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
