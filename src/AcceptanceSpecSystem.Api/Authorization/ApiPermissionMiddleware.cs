using System.Text.Json;
using AcceptanceSpecSystem.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace AcceptanceSpecSystem.Api.Authorization;

/// <summary>
/// API 权限中间件：基于控制器动作推导权限码并执行强校验（默认拒绝）
/// </summary>
public sealed class ApiPermissionMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ApiPermissionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint == null)
        {
            await _next(context);
            return;
        }

        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null)
        {
            await _next(context);
            return;
        }

        var descriptor = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (descriptor == null)
        {
            await _next(context);
            return;
        }

        if (!(context.User.Identity?.IsAuthenticated ?? false))
        {
            await _next(context);
            return;
        }

        var requiredPermission = PermissionConventions.ResolveApiPermissionCode(descriptor);
        var grantedPermissions = context.User
            .FindAll("permission")
            .Select(c => c.Value)
            .ToArray();

        if (!PermissionMatcher.HasPermission(grantedPermissions, requiredPermission))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json; charset=utf-8";
            var payload = ApiResponse.Error(403, $"无权限访问，缺少权限：{requiredPermission}");
            await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
            return;
        }

        context.Items["RequiredPermission"] = requiredPermission;
        await _next(context);
    }
}
