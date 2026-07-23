namespace AcceptanceSpecSystem.Application.Options;

public sealed class EmbeddingCacheWarmupOptions
{
    public const string SectionName = "EmbeddingCacheWarmup";
    public bool Enabled { get; set; }
    public bool RunOnStartup { get; set; }
    public string? RunAtLocalTime { get; set; }
    public int IntervalHours { get; set; } = 24;
    public int BatchSize { get; set; } = 100;
    public int MaxItemsPerRun { get; set; } = 1000;
}
