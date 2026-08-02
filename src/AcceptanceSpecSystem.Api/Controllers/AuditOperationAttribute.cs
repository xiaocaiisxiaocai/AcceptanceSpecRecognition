using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Middleware;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Diagnostics;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 审计操作标记（仅用于增删改类控制器动作）
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class AuditOperationAttribute : Attribute
{
    /// <summary>
    /// 操作名称（create/update/delete/import/execute 等）
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// 业务对象名称
    /// </summary>
    public string Resource { get; }

    /// <summary>
    /// 是否记录成功请求；失败请求始终记录。
    /// </summary>
    public bool RecordSuccessful { get; }

    public AuditOperationAttribute(
        string operation,
        string resource,
        bool recordSuccessful = true)
    {
        Operation = operation;
        Resource = resource;
        RecordSuccessful = recordSuccessful;
    }
}

/// <summary>
/// 控制器级审计过滤器：仅记录带 <see cref="AuditOperationAttribute"/> 的动作
/// </summary>
internal sealed record AuditOperationState(
    AuditOperationAttribute Attribute,
    string Controller,
    string? Action,
    IReadOnlyDictionary<string, string?> RouteValues,
    string? Username,
    long StartedTimestamp);

public sealed class AuditOperationFilter :
    IAsyncActionFilter,
    IAsyncAlwaysRunResultFilter,
    IAsyncExceptionFilter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly object AuditStateKey = new();
    private static readonly object AuditExceptionKey = new();
    private static readonly object AuditWrittenKey = new();
    private static readonly object AuditSafeDetailsKey = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditOperationFilter> _logger;

    public AuditOperationFilter(
        IServiceScopeFactory scopeFactory,
        ILogger<AuditOperationFilter> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    internal static void SetSafeDetails(HttpContext httpContext, object details)
    {
        httpContext.Items[AuditSafeDetailsKey] = details;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var descriptor = context.ActionDescriptor as ControllerActionDescriptor;
        var auditAttr = descriptor?.MethodInfo
            .GetCustomAttributes(typeof(AuditOperationAttribute), inherit: true)
            .OfType<AuditOperationAttribute>()
            .FirstOrDefault();

        if (auditAttr == null)
        {
            await next();
            return;
        }

        var httpContext = context.HttpContext;
        httpContext.Items[AuditStateKey] = new AuditOperationState(
            auditAttr,
            context.Controller.GetType().Name,
            context.ActionDescriptor.DisplayName,
            context.RouteData.Values.ToDictionary(
                pair => pair.Key,
                pair => pair.Value?.ToString()),
            ResolveAuditUsername(httpContext, context.ActionArguments),
            Stopwatch.GetTimestamp());

        await next();
    }

    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next)
    {
        var executed = await next();
        CaptureException(context.HttpContext, executed.Exception);
    }

    public Task OnExceptionAsync(ExceptionContext context)
    {
        CaptureException(context.HttpContext, context.Exception);
        return Task.CompletedTask;
    }

    internal Task WriteFinalAuditAsync(HttpContext httpContext, Exception? exception = null)
    {
        if (exception == null &&
            httpContext.Items.TryGetValue(AuditExceptionKey, out var exceptionValue))
        {
            exception = exceptionValue as Exception;
        }

        return TryWriteOnceAsync(
            httpContext,
            httpContext.Response.StatusCode,
            exception,
            exception == null ? httpContext.RequestAborted : CancellationToken.None);
    }

    private static void CaptureException(HttpContext httpContext, Exception? exception)
    {
        if (exception != null)
        {
            httpContext.Items[AuditExceptionKey] = exception;
        }
    }

    private async Task TryWriteOnceAsync(
        HttpContext httpContext,
        int statusCode,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        if (!httpContext.Items.TryGetValue(AuditStateKey, out var stateValue) ||
            stateValue is not AuditOperationState state ||
            httpContext.Items.ContainsKey(AuditWrittenKey))
        {
            return;
        }

        httpContext.Items[AuditWrittenKey] = true;

        try
        {
            var level = ResolveLevel(statusCode, exception);
            if (!state.Attribute.RecordSuccessful &&
                level == AuditLogLevel.Information)
            {
                return;
            }

            var detailsPayload = new
            {
                operation = state.Attribute.Operation,
                resource = state.Attribute.Resource,
                controller = state.Controller,
                action = state.Action,
                routeValues = state.RouteValues,
                operationDetails = httpContext.Items.TryGetValue(
                    AuditSafeDetailsKey,
                    out var safeDetails)
                    ? safeDetails
                    : null,
                error = exception == null
                    ? null
                    : SensitiveLogFormatter.SanitizeMessage(
                        exception.Message,
                        exception.GetType().Name)
            };

            var command = new AuditTrailWriteCommand(
                AuditLogSource.BackendRequest,
                level,
                $"controller.{state.Attribute.Operation}",
                state.Username,
                httpContext.Request.Method,
                httpContext.Request.Path.Value,
                httpContext.Request.QueryString.HasValue ? httpContext.Request.QueryString.Value : null,
                statusCode,
                (long)Stopwatch.GetElapsedTime(state.StartedTimestamp).TotalMilliseconds,
                httpContext.Connection.RemoteIpAddress?.ToString(),
                httpContext.Request.Headers.UserAgent.ToString(),
                ResolveTraceId(httpContext),
                httpContext.Request.Headers["X-Client-Id"].FirstOrDefault(),
                httpContext.Request.Headers["X-Frontend-Route"].FirstOrDefault(),
                JsonSerializer.Serialize(detailsPayload, JsonOptions),
                DateTime.UtcNow);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var auditTrail = scope.ServiceProvider.GetRequiredService<IAuditTrailAppService>();
            await auditTrail.WriteAsync(command, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入控制器审计日志失败: {Path}", httpContext.Request.Path);
        }
    }

    private static string? ResolveAuditUsername(HttpContext httpContext, IDictionary<string, object?> actionArguments)
    {
        var username = GetCurrentUsername(httpContext.User);
        if (!string.IsNullOrWhiteSpace(username))
            return username;

        if (httpContext.Items.TryGetValue("AuditUsername", out var itemUsername) &&
            itemUsername is string fromItem &&
            !string.IsNullOrWhiteSpace(fromItem))
        {
            return fromItem.Trim();
        }

        foreach (var value in actionArguments.Values)
        {
            if (value == null)
                continue;

            var type = value.GetType();
            var usernameProperty = type.GetProperty("Username");
            if (usernameProperty?.PropertyType != typeof(string))
                continue;

            if (usernameProperty.GetValue(value) is string fromArg &&
                !string.IsNullOrWhiteSpace(fromArg))
            {
                return fromArg.Trim();
            }
        }

        return null;
    }

    private static AuditLogLevel ResolveLevel(int statusCode, Exception? exception)
    {
        if (exception != null || statusCode >= 500)
            return AuditLogLevel.Error;
        if (statusCode >= 400)
            return AuditLogLevel.Warning;
        return AuditLogLevel.Information;
    }

    private static string? GetCurrentUsername(ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? user.FindFirstValue(ClaimTypes.Name)
               ?? user.FindFirstValue("sub");
    }

    private static string? ResolveTraceId(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue(RequestTracingMiddleware.TraceIdItemKey, out var traceId) &&
            traceId is string value &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return httpContext.Request.Headers["X-Client-Trace-Id"].FirstOrDefault()
               ?? httpContext.TraceIdentifier;
    }
}
