namespace AcceptanceSpecSystem.Api.Options;

/// <summary>
/// 在宿主构建阶段拒绝浏览器无法接受或会削弱 Cookie/CSRF 防护的配置组合。
/// 独立类型便于对与 WebApplicationFactory 配置时序无关的纯规则做单元测试。
/// </summary>
public static class BrowserAuthConfigurationGuard
{
    public static void Validate(
        BrowserAuthOptions options,
        IReadOnlyCollection<string> allowedOrigins,
        bool isProduction)
    {
        if (string.IsNullOrWhiteSpace(options.RefreshCookieName) ||
            string.IsNullOrWhiteSpace(options.CsrfCookieName) ||
            string.IsNullOrWhiteSpace(options.CsrfHeaderName))
            throw new InvalidOperationException("BrowserAuth Cookie 与 CSRF 名称不能为空");

        if (string.IsNullOrWhiteSpace(options.CookiePath) || !options.CookiePath.StartsWith('/'))
            throw new InvalidOperationException("BrowserAuth:CookiePath 必须是以 / 开头的绝对路径");

        ValidateCookiePrefix(options.RefreshCookieName, "Refresh", options);
        ValidateCookiePrefix(options.CsrfCookieName, "CSRF", options);

        if (options.CookieSameSite == SameSiteMode.None && !options.CookieSecure)
            throw new InvalidOperationException("BrowserAuth:CookieSameSite=None 时必须启用 Secure");

        if (allowedOrigins.Count == 0 || allowedOrigins.Any(origin => !IsExactOrigin(origin, out _)))
            throw new InvalidOperationException("BrowserAuth:AllowedOrigins 必须配置不含通配符的精确 HTTP(S) 来源");

        if (options.AllowInsecureHttp)
        {
            if (options.CookieSecure)
                throw new InvalidOperationException("内网 HTTP 模式必须关闭 BrowserAuth:CookieSecure");
            if (options.CookieSameSite != SameSiteMode.Strict)
                throw new InvalidOperationException("内网 HTTP 模式必须使用 BrowserAuth:CookieSameSite=Strict");
            if (!string.IsNullOrWhiteSpace(options.CookieDomain))
                throw new InvalidOperationException("内网 HTTP 模式禁止设置 BrowserAuth:CookieDomain，必须使用 host-only Cookie");
            if (options.CookiePath != "/")
                throw new InvalidOperationException("内网 HTTP 模式必须使用 BrowserAuth:CookiePath=/");
            if (allowedOrigins.Any(origin => !IsExactOrigin(origin, out var uri) || uri!.Scheme != Uri.UriSchemeHttp))
                throw new InvalidOperationException("内网 HTTP 模式仅允许精确的 HTTP 来源，禁止 HTTPS 或混合协议");
        }

        if (isProduction && !options.AllowInsecureHttp)
        {
            if (!options.CookieSecure)
                throw new InvalidOperationException("Production 必须启用 BrowserAuth:CookieSecure");
            if (allowedOrigins.Any(origin => !origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Production BrowserAuth:AllowedOrigins 仅允许 HTTPS 来源");
        }

    }

    private static void ValidateCookiePrefix(string cookieName, string label, BrowserAuthOptions options)
    {
        if (cookieName.StartsWith("__Host-", StringComparison.Ordinal) &&
            (!options.CookieSecure || !string.IsNullOrWhiteSpace(options.CookieDomain) || options.CookiePath != "/"))
            throw new InvalidOperationException($"__Host- {label} Cookie 必须启用 Secure、不得设置 Domain 且 Path 必须为 /");

        if (cookieName.StartsWith("__Secure-", StringComparison.Ordinal) && !options.CookieSecure)
            throw new InvalidOperationException($"__Secure- {label} Cookie 必须启用 Secure");
    }

    private static bool IsExactOrigin(string origin, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(origin) || origin.Contains('*') ||
            !Uri.TryCreate(origin.Trim(), UriKind.Absolute, out var candidate) ||
            (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(candidate.UserInfo) || candidate.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(candidate.Query) || !string.IsNullOrEmpty(candidate.Fragment))
            return false;

        var normalizedInput = origin.Trim().TrimEnd('/');
        var authority = candidate.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        if (!normalizedInput.Equals(authority, StringComparison.OrdinalIgnoreCase))
            return false;

        uri = candidate;
        return true;
    }
}
