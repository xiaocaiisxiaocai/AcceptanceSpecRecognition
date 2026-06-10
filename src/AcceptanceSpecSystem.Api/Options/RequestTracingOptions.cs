namespace AcceptanceSpecSystem.Api.Options;

/// <summary>
/// 请求追踪配置。
/// </summary>
public sealed class RequestTracingOptions
{
    public const string SectionName = "RequestTracing";

    public string HeaderName { get; set; } = "X-Trace-Id";

    public string ClientHeaderName { get; set; } = "X-Client-Trace-Id";
}
