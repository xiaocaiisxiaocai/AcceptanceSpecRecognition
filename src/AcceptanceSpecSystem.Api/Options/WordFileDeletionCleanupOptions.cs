namespace AcceptanceSpecSystem.Api.Options;

public sealed class WordFileDeletionCleanupOptions
{
    public const string SectionName = "WordFileDeletionCleanup";

    public bool Enabled { get; set; } = true;
    public int InitialDelaySeconds { get; set; } = 30;
    public int CleanupIntervalMinutes { get; set; } = 1;
    public int BatchSize { get; set; } = 100;
}
