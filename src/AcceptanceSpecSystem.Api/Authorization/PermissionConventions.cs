using AcceptanceSpecSystem.Api.Controllers;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;

namespace AcceptanceSpecSystem.Api.Authorization;

/// <summary>
/// 将 ASP.NET Core action 元数据映射到 Application 权限编码约定。
/// </summary>
public static class PermissionConventions
{
    public static string ResolveApiPermissionCode(ControllerActionDescriptor descriptor)
    {
        var auditAttr = descriptor.MethodInfo
            .GetCustomAttributes(typeof(AuditOperationAttribute), true)
            .OfType<AuditOperationAttribute>()
            .FirstOrDefault();

        return PermissionCodeConventions.ResolveApiPermissionCode(
            descriptor.ControllerName,
            descriptor.ActionName,
            descriptor.AttributeRouteInfo?.Template,
            descriptor.EndpointMetadata.OfType<HttpMethodAttribute>()
                .SelectMany(attribute => attribute.HttpMethods)
                .FirstOrDefault(),
            auditAttr?.Resource,
            auditAttr?.Operation);
    }

    public static string ResolveApiPermissionCode(
        string controllerName,
        string actionName,
        string? routeTemplate,
        string? httpMethod,
        string? resourceOverride = null,
        string? actionOverride = null) =>
        PermissionCodeConventions.ResolveApiPermissionCode(
            controllerName,
            actionName,
            routeTemplate,
            httpMethod,
            resourceOverride,
            actionOverride);

    public static string BuildButtonPermissionCode(string apiPermissionCode) =>
        PermissionCodeConventions.BuildButtonPermissionCode(apiPermissionCode);
}
