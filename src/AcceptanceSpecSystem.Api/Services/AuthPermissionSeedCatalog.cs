using System.Reflection;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Controllers;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 将导航清单和 ASP.NET Action 元数据转换为 Application 权限种子定义。
/// </summary>
public sealed class AuthPermissionSeedCatalog : IAuthPermissionSeedCatalog
{
    private readonly IHostEnvironment _environment;

    public AuthPermissionSeedCatalog(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public IReadOnlyCollection<AuthPermissionSeedDefinition> GetSeeds()
    {
        var seeds = new Dictionary<string, AuthPermissionSeedDefinition>(StringComparer.OrdinalIgnoreCase);
        var manifest = LoadManifest();
        foreach (var page in manifest.Pages)
            seeds[page.Code] = new(page.Code, $"页面-{page.Title}", PermissionType.Page,
                page.Resource, page.Action, RoutePath: page.Path);
        foreach (var menu in manifest.Menus)
            seeds[menu.Code] = new(menu.Code, $"菜单-{menu.Title}", PermissionType.Menu,
                menu.Resource, menu.Action, RoutePath: menu.Path);

        foreach (var action in BuildApiActions())
        {
            var code = PermissionConventions.ResolveApiPermissionCode(
                action.ControllerName, action.ActionName, action.RouteTemplate, action.HttpMethod,
                action.ResourceOverride, action.ActionOverride);
            var segments = code.Split(':', StringSplitOptions.TrimEntries);
            var resource = segments[1];
            var operation = segments[2];
            seeds[code] = new(code, $"接口-{resource}-{operation}", PermissionType.Api,
                resource, operation, HttpMethod: action.HttpMethod,
                ApiPath: "/" + action.RouteTemplate.Trim('/'));

            if (operation is not ("read" or "login" or "refresh-token"))
            {
                var button = PermissionConventions.BuildButtonPermissionCode(code);
                seeds[button] = new(button, $"按钮-{resource}-{operation}", PermissionType.Button,
                    resource, operation);
            }
        }

        return seeds.Values.ToList();
    }

    private NavigationManifest LoadManifest()
    {
        foreach (var root in new[] { _environment.ContentRootPath, AppContext.BaseDirectory, Directory.GetCurrentDirectory() }
                     .Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var current = new DirectoryInfo(root);
            while (current is not null)
            {
                var path = Path.Combine(current.FullName, "shared", "navigation", "navigation-manifest.json");
                if (File.Exists(path))
                {
                    return JsonSerializer.Deserialize<NavigationManifest>(File.ReadAllText(path),
                               new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                           ?? throw new InvalidOperationException($"导航清单解析失败: {path}");
                }
                current = current.Parent;
            }
        }

        throw new InvalidOperationException("未找到 shared/navigation/navigation-manifest.json");
    }

    private static IReadOnlyCollection<ApiActionSeed> BuildApiActions()
    {
        var results = new List<ApiActionSeed>();
        foreach (var controllerType in typeof(Program).Assembly.GetTypes()
                     .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type)))
        {
            var controllerName = controllerType.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)
                ? controllerType.Name[..^10]
                : controllerType.Name;
            var controllerRoute = ResolveControllerRoute(controllerType);

            foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                         .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NonActionAttribute>(true) == null))
            {
                var audit = method.GetCustomAttribute<AuditOperationAttribute>(true);
                foreach (var httpAttribute in method.GetCustomAttributes(true).OfType<HttpMethodAttribute>())
                {
                    var route = CombineRoute(controllerRoute, httpAttribute.Template, controllerName, method.Name);
                    foreach (var httpMethod in httpAttribute.HttpMethods.DefaultIfEmpty("GET"))
                    {
                        results.Add(new ApiActionSeed(controllerName, method.Name,
                            string.IsNullOrWhiteSpace(route) ? "/" : route,
                            httpMethod.ToUpperInvariant(), audit?.Resource, audit?.Operation));
                    }
                }
            }
        }

        return results.DistinctBy(item =>
            $"{item.ControllerName}.{item.ActionName}.{item.HttpMethod}.{item.RouteTemplate}").ToList();
    }

    private static string ResolveControllerRoute(Type controllerType)
    {
        for (var current = controllerType; current is not null; current = current.BaseType)
        {
            var route = current.GetCustomAttributes<RouteAttribute>(false).FirstOrDefault();
            if (route is not null) return route.Template ?? string.Empty;
        }
        return string.Empty;
    }

    private static string CombineRoute(string controller, string? action, string controllerName, string actionName)
    {
        controller = ReplaceTokens(controller, controllerName, actionName);
        action = ReplaceTokens(action ?? string.Empty, controllerName, actionName);
        if (action.StartsWith("~/", StringComparison.Ordinal)) return action[2..].Trim('/');
        if (action.StartsWith("/", StringComparison.Ordinal)) return action.Trim('/');
        if (string.IsNullOrWhiteSpace(action)) return controller.Trim('/');
        if (string.IsNullOrWhiteSpace(controller)) return action.Trim('/');
        return $"{controller.TrimEnd('/')}/{action.TrimStart('/')}";
    }

    private static string ReplaceTokens(string value, string controller, string action) => value.Trim()
        .Replace("[controller]", controller, StringComparison.OrdinalIgnoreCase)
        .Replace("[action]", action, StringComparison.OrdinalIgnoreCase);

    private sealed record ApiActionSeed(string ControllerName, string ActionName, string RouteTemplate,
        string HttpMethod, string? ResourceOverride, string? ActionOverride);

    private sealed class NavigationManifest
    {
        public List<NavigationItem> Menus { get; set; } = [];
        public List<NavigationItem> Pages { get; set; } = [];
    }

    private sealed class NavigationItem
    {
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Resource { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }
}
