namespace AcceptanceSpecSystem.Api.Options;

/// <summary>
/// 浏览器 Cookie 认证与限时兼容配置。
/// </summary>
public sealed class BrowserAuthOptions
{
    public const string SectionName = "BrowserAuth";

    public string RefreshCookieName { get; set; } = "__Host-acceptance-refresh";

    public string CsrfCookieName { get; set; } = "acceptance-csrf";

    public string CsrfHeaderName { get; set; } = "X-CSRF-Token";

    public string CookiePath { get; set; } = "/";

    public string? CookieDomain { get; set; }

    public SameSiteMode CookieSameSite { get; set; } = SameSiteMode.Strict;

    public bool CookieSecure { get; set; } = true;

    /// <summary>
    /// 仅用于受控内网的显式 HTTP 降级。默认关闭；开启后仍强制 Strict、host-only 与精确 HTTP Origin。
    /// </summary>
    public bool AllowInsecureHttp { get; set; }

    public string[] AllowedOrigins { get; set; } = [];
}
