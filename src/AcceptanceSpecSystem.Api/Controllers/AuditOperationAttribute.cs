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

    public AuditOperationAttribute(string operation, string resource)
    {
        Operation = operation;
        Resource = resource;
    }
}

/// <summary>
/// 控制器级审计过滤器：仅记录带 <see cref="AuditOperationAttribute"/> 的动作
/// </summary>
public sealed class AuditOperationFilter : IAsyncActionFilter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAuditTrailAppService _auditTrail;
    private readonly ILogger<AuditOperationFilter> _logger;

    public AuditOperationFilter(IAuditTrailAppService auditTrail, ILogger<AuditOperationFilter> logger)
    {
        _auditTrail = auditTrail;
        _logger = logger;
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

        var stopwatch = Stopwatch.StartNew();
        ActionExecutedContext? executedContext = null;
        try
        {
            executedContext = await next();
        }
        finally
        {
            stopwatch.Stop();
            await TryWriteAuditAsync(context, executedContext, auditAttr, stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task TryWriteAuditAsync(
        ActionExecutingContext context,
        ActionExecutedContext? executedContext,
        AuditOperationAttribute attr,
        long durationMs)
    {
        try
        {
            var httpContext = context.HttpContext;
            var statusCode = httpContext.Response.StatusCode;
            var level = ResolveLevel(statusCode, executedContext?.Exception);

            var routeValues = context.RouteData.Values
                .ToDictionary(k => k.Key, v => v.Value?.ToString());

            var detailsPayload = new
            {
                operation = attr.Operation,
                resource = attr.Resource,
                controller = context.Controller.GetType().Name,
                action = context.ActionDescriptor.DisplayName,
                routeValues,
                error = executedContext?.Exception == null
                    ? null
                    : SensitiveLogFormatter.SanitizeMessage(
                        executedContext.Exception.Message,
                        executedContext.Exception.GetType().Name)
            };

            var username = ResolveAuditUsername(httpContext, context.ActionArguments);

            await _auditTrail.WriteAsync(new AuditTrailWriteCommand(
                AuditLogSource.BackendRequest,
                level,
                $"controller.{attr.Operation}",
                username,
                httpContext.Request.Method,
                httpContext.Request.Path.Value,
                httpContext.Request.QueryString.HasValue ? httpContext.Request.QueryString.Value : null,
                statusCode,
                durationMs,
                httpContext.Connection.RemoteIpAddress?.ToString(),
                httpContext.Request.Headers.UserAgent.ToString(),
                ResolveTraceId(httpContext),
                httpContext.Request.Headers["X-Client-Id"].FirstOrDefault(),
                httpContext.Request.Headers["X-Frontend-Route"].FirstOrDefault(),
                JsonSerializer.Serialize(detailsPayload, JsonOptions),
                DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入控制器审计日志失败: {Path}", context.HttpContext.Request.Path);
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
