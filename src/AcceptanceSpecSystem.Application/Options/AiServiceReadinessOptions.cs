namespace AcceptanceSpecSystem.Application.Options;

public sealed class AiServiceReadinessOptions
{
    public const string SectionName = "AiServiceReadiness";

    public int StatusTtlSeconds { get; set; } = 30;

    public int ProbeTimeoutSeconds { get; set; } = 20;

    public int MaxConcurrentProbes { get; set; } = 2;

    public bool PreloadOnStartup { get; set; } = true;
}
