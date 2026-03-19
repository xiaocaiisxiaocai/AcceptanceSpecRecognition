using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Api.Controllers;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;

namespace AcceptanceSpecSystem.Api.Authorization;

/// <summary>
/// 权限编码约定
/// </summary>
public static class PermissionConventions
{
    public static string ResolveApiPermissionCode(ControllerActionDescriptor descriptor)
    {
        var auditAttr = descriptor.MethodInfo
            .GetCustomAttributes(typeof(AuditOperationAttribute), true)
            .OfType<AuditOperationAttribute>()
            .FirstOrDefault();

        return ResolveApiPermissionCode(
            controllerName: descriptor.ControllerName,
            actionName: descriptor.ActionName,
            routeTemplate: descriptor.AttributeRouteInfo?.Template,
            httpMethod: descriptor.EndpointMetadata
                .OfType<HttpMethodAttribute>()
                .SelectMany(attr => attr.HttpMethods)
                .FirstOrDefault(),
            resourceOverride: auditAttr?.Resource,
            actionOverride: auditAttr?.Operation);
    }

    public static string ResolveApiPermissionCode(
        string controllerName,
        string actionName,
        string? routeTemplate,
        string? httpMethod,
        string? resourceOverride = null,
        string? actionOverride = null)
    {
        var resource = string.IsNullOrWhiteSpace(resourceOverride)
            ? ResolveResource(controllerName, routeTemplate)
            : NormalizeSegment(resourceOverride);
        var action = string.IsNullOrWhiteSpace(actionOverride)
            ? ResolveAction(actionName, routeTemplate, httpMethod)
            : NormalizeSegment(actionOverride);
        return $"api:{resource}:{action}";
    }

    public static string BuildButtonPermissionCode(string apiPermissionCode)
    {
        if (!apiPermissionCode.StartsWith("api:", StringComparison.OrdinalIgnoreCase))
            return apiPermissionCode;
        return $"btn:{apiPermissionCode[4..]}";
    }

    private static string ResolveResource(string controllerName, string? routeTemplate)
    {
        if (!string.IsNullOrWhiteSpace(routeTemplate))
        {
            var normalizedRoute = routeTemplate
                .Replace("{", "/", StringComparison.Ordinal)
                .Trim('/');
            var segments = normalizedRoute
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var apiIndex = Array.FindIndex(segments, seg => seg.Equals("api", StringComparison.OrdinalIgnoreCase));
            if (apiIndex >= 0 && apiIndex + 1 < segments.Length)
            {
                return NormalizeResourceSegment(segments[apiIndex + 1]);
            }
        }

        return NormalizeResourceSegment(ToKebabCase(controllerName));
    }

    private static string ResolveAction(string actionName, string? routeTemplate, string? httpMethod)
    {
        var normalizedRouteTemplate = routeTemplate?.ToLowerInvariant() ?? string.Empty;
        var normalizedActionName = actionName.ToLowerInvariant();
        var normalizedHttpMethod = string.IsNullOrWhiteSpace(httpMethod)
            ? "GET"
            : httpMethod.ToUpperInvariant();

        if (normalizedActionName.Contains("getasyncroutes", StringComparison.OrdinalIgnoreCase) ||
            normalizedRouteTemplate.Contains("get-async-routes", StringComparison.Ordinal))
        {
            return "routes";
        }

        if (normalizedActionName.Contains("refresh", StringComparison.OrdinalIgnoreCase) ||
            normalizedRouteTemplate.Contains("refresh-token", StringComparison.Ordinal))
        {
            return "refresh-token";
        }

        if (normalizedActionName.Contains("login", StringComparison.OrdinalIgnoreCase) ||
            normalizedRouteTemplate.EndsWith("login", StringComparison.Ordinal))
        {
            return "login";
        }

        if (normalizedRouteTemplate.Contains("set-default", StringComparison.Ordinal) ||
            normalizedActionName.Contains("setdefault", StringComparison.Ordinal))
        {
            return "set-default";
        }

        if (normalizedRouteTemplate.Contains("status", StringComparison.Ordinal) ||
            normalizedActionName.Contains("status", StringComparison.Ordinal))
        {
            return "update-status";
        }

        if (normalizedRouteTemplate.Contains("password", StringComparison.Ordinal) ||
            normalizedActionName.Contains("password", StringComparison.Ordinal))
        {
            return "reset-password";
        }

        if (normalizedRouteTemplate.Contains("batch-import", StringComparison.Ordinal))
        {
            return "import-batch";
        }

        if (normalizedRouteTemplate.Contains("batch-preview", StringComparison.Ordinal))
        {
            return "preview-batch";
        }

        if (normalizedRouteTemplate.Contains("batch-execute", StringComparison.Ordinal))
        {
            return "execute-batch";
        }

        if (normalizedRouteTemplate.Contains("delete-batch", StringComparison.Ordinal) ||
            (normalizedRouteTemplate.EndsWith("batch", StringComparison.Ordinal) && normalizedHttpMethod == "DELETE"))
        {
            return "delete-batch";
        }

        if (normalizedRouteTemplate.Contains("llm-stream", StringComparison.Ordinal))
        {
            return "llm-stream";
        }

        if (normalizedRouteTemplate.Contains("similarity", StringComparison.Ordinal))
        {
            return "similarity";
        }

        if (normalizedRouteTemplate.Contains("preview", StringComparison.Ordinal))
        {
            return "preview";
        }

        if (normalizedRouteTemplate.Contains("execute", StringComparison.Ordinal))
        {
            return "execute";
        }

        if (normalizedRouteTemplate.Contains("download", StringComparison.Ordinal))
        {
            return "download";
        }

        if (normalizedRouteTemplate.Contains("upload", StringComparison.Ordinal))
        {
            return "upload";
        }

        if (normalizedRouteTemplate.Contains("import", StringComparison.Ordinal))
        {
            return "import";
        }

        if (normalizedRouteTemplate.Contains("test", StringComparison.Ordinal))
        {
            return "test";
        }

        if (normalizedRouteTemplate.Contains("models", StringComparison.Ordinal))
        {
            return "models";
        }

        if (normalizedRouteTemplate.Contains("effective", StringComparison.Ordinal))
        {
            return "effective";
        }

        if (normalizedRouteTemplate.Contains("default", StringComparison.Ordinal))
        {
            return "default";
        }

        if (normalizedRouteTemplate.Contains("reset", StringComparison.Ordinal))
        {
            return "reset";
        }

        return normalizedHttpMethod switch
        {
            "GET" => "read",
            "POST" => "create",
            "PUT" => "update",
            "PATCH" => "update",
            "DELETE" => "delete",
            _ => NormalizeSegment(normalizedHttpMethod.ToLowerInvariant())
        };
    }

    private static string NormalizeResourceSegment(string value)
    {
        var kebab = ToKebabCase(value);
        if (kebab.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
            return kebab[..^3] + "y";
        if (kebab.EndsWith("ses", StringComparison.OrdinalIgnoreCase))
            return kebab[..^2];
        if (kebab.EndsWith("s", StringComparison.OrdinalIgnoreCase) &&
            !kebab.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
        {
            return kebab[..^1];
        }

        return kebab;
    }

    private static string NormalizeSegment(string value)
    {
        return Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9\\-:]+", "-");
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Replace("_", "-", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, "([a-z0-9])([A-Z])", "$1-$2");
        normalized = Regex.Replace(normalized, "-{2,}", "-");
        return normalized.Trim('-').ToLowerInvariant();
    }
}
