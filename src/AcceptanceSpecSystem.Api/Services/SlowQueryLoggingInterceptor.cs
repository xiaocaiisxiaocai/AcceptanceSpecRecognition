using System.Data.Common;
using AcceptanceSpecSystem.Api.Middleware;
using AcceptanceSpecSystem.Api.Options;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 记录超过阈值的 EF Core 查询，便于定位性能隐患。
/// </summary>
public sealed class SlowQueryLoggingInterceptor : DbCommandInterceptor
{
    private readonly SlowQueryOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SlowQueryLoggingInterceptor> _logger;

    public SlowQueryLoggingInterceptor(
        IOptions<SlowQueryOptions> options,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SlowQueryLoggingInterceptor> logger)
    {
        _options = options.Value;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        LogIfSlow(command, eventData);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        LogIfSlow(command, eventData);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        LogIfSlow(command, eventData);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    private void LogIfSlow(DbCommand command, CommandExecutedEventData eventData)
    {
        if (!_options.Enabled)
            return;

        var thresholdMs = GetThresholdMilliseconds(_options);
        var elapsedMs = eventData.Duration.TotalMilliseconds;
        if (!ShouldLog(_options, eventData.Duration))
            return;

        var httpContext = _httpContextAccessor.HttpContext;
        var traceId = httpContext?.Items[RequestTracingMiddleware.TraceIdItemKey]?.ToString()
                      ?? httpContext?.TraceIdentifier
                      ?? string.Empty;
        var path = httpContext?.Request.Path.Value ?? string.Empty;

        if (_options.IncludeSqlText)
        {
            _logger.LogWarning(
                "慢查询: {ElapsedMs}ms >= {ThresholdMs}ms, TraceId={TraceId}, Path={Path}, Sql={Sql}",
                Math.Round(elapsedMs, 2),
                thresholdMs,
                traceId,
                path,
                command.CommandText);
            return;
        }

        _logger.LogWarning(
            "慢查询: {ElapsedMs}ms >= {ThresholdMs}ms, TraceId={TraceId}, Path={Path}, CommandType={CommandType}",
            Math.Round(elapsedMs, 2),
            thresholdMs,
            traceId,
            path,
            command.CommandType);
    }

    public static bool ShouldLog(SlowQueryOptions options, TimeSpan duration)
    {
        return options.Enabled && duration.TotalMilliseconds >= GetThresholdMilliseconds(options);
    }

    public static int GetThresholdMilliseconds(SlowQueryOptions options)
    {
        return Math.Max(1, options.ThresholdMilliseconds);
    }
}
