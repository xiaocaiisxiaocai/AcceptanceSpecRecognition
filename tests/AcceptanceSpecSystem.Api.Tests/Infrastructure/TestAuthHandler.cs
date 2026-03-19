using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Tests.Infrastructure;

/// <summary>
/// API 集成测试专用鉴权处理器。
/// 默认授予 admin 角色，可通过请求头 X-Test-Role 覆盖。
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAuth";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authMode = Request.Headers.TryGetValue("X-Test-Auth", out var authModeValues)
            ? authModeValues.ToString()
            : string.Empty;
        if (string.Equals(authMode, "anonymous", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var role = Request.Headers.TryGetValue("X-Test-Role", out var roleValues)
            ? roleValues.ToString()
            : "admin";
        if (string.IsNullOrWhiteSpace(role))
            role = "admin";

        var normalizedRole = role.Trim().ToLowerInvariant();
        var userId = normalizedRole == "admin" ? "1" : "2";
        var permissionsHeader = Request.Headers.TryGetValue("X-Test-Permissions", out var permissionValues)
            ? permissionValues.ToString()
            : string.Empty;
        var permissions = !string.IsNullOrWhiteSpace(permissionsHeader)
            ? permissionsHeader
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : normalizedRole == "admin"
                ? new[] { "*:*:*" }
                : new[] { "api:auth:routes", "page:home:dashboard" };

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "integration-test-user"),
            new(ClaimTypes.Name, "integration-test-user"),
            new("user_id", userId),
            new("company_id", "1"),
            new("permission_version", "1"),
            new(ClaimTypes.Role, role),
        };

        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
