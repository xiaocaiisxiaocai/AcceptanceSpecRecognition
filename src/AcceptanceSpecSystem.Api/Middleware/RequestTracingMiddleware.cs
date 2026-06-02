using System.Diagnostics;
using AcceptanceSpecSystem.Api.Options;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Middleware;

/// <summary>
/// 为每个请求建立可关联的 traceId，并回写到响应头。
/// </summary>
public sealed class RequestTracingMiddleware
{
    public const string TraceIdItemKey = "TraceId";

    private readonly RequestDelegate _next;
    private readonly RequestTracingOptions _options;
    private readonly ILogger<RequestTracingMiddleware> _logger;

    public RequestTracingMiddleware(
        RequestDelegate next,
        IOptions<RequestTracingOptions> options,
        ILogger<RequestTracingMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = ResolveTraceId(context);
        context.Items[TraceIdItemKey] = traceId;
        Activity.Current?.SetTag("traceId", traceId);

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[_options.HeaderName] = traceId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object>
               {
                   ["traceId"] = traceId,
                   ["requestPath"] = context.Request.Path.Value ?? string.Empty
               }))
        {
            await _next(context);
        }
    }

    private string ResolveTraceId(HttpContext context)
    {
        var fromClient = context.Request.Headers[_options.ClientHeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fromClient))
        {
            return TrimTraceId(fromClient);
        }

        return Activity.Current?.TraceId.ToString()
               ?? context.TraceIdentifier
               ?? Guid.NewGuid().ToString("N");
    }

    private static string TrimTraceId(string value)
    {
        value = value.Trim();
        return value.Length <= 64 ? value : value[..64];
    }
}
