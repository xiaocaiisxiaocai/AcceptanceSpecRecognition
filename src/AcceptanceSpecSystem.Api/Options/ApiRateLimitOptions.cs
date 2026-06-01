namespace AcceptanceSpecSystem.Api.Options;

public sealed class ApiRateLimitOptions
{
    public const string SectionName = "ApiRateLimits";

    public RateLimitPolicyOptions Login { get; set; } = new()
    {
        PermitLimit = 10,
        WindowSeconds = 60,
        QueueLimit = 0
    };

    public RateLimitPolicyOptions Upload { get; set; } = new()
    {
        PermitLimit = 20,
        WindowSeconds = 60,
        QueueLimit = 0
    };

    public RateLimitPolicyOptions AiHeavy { get; set; } = new()
    {
        PermitLimit = 30,
        WindowSeconds = 60,
        QueueLimit = 0
    };
}

public sealed class RateLimitPolicyOptions
{
    public int PermitLimit { get; set; } = 10;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; }
}
