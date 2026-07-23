using System.Security.Cryptography;
using System.Text;
using AcceptanceSpecSystem.Api.Options;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

public interface IBrowserAuthSecurityService
{
    bool ValidateTrustedOrigin(HttpRequest request, out string error);
    bool ValidateStateChangingRequest(HttpRequest request, out string error);
    void WriteSessionCookies(HttpResponse response, string refreshToken, DateTime refreshExpiresAt);
    void ClearSessionCookies(HttpResponse response);
}

public sealed class BrowserAuthSecurityService : IBrowserAuthSecurityService
{
    private readonly BrowserAuthOptions _options;
    private readonly HashSet<string> _allowedOrigins;

    public BrowserAuthSecurityService(IOptions<BrowserAuthOptions> options)
    {
        _options = options.Value;
        _allowedOrigins = _options.AllowedOrigins.Select(NormalizeOrigin).Where(origin => origin != null)
            .Select(origin => origin!).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool ValidateStateChangingRequest(HttpRequest request, out string error)
    {
        if (!ValidateTrustedOrigin(request, out error))
            return false;

        var csrfCookie = request.Cookies[_options.CsrfCookieName];
        var csrfHeader = request.Headers[_options.CsrfHeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(csrfCookie) || string.IsNullOrWhiteSpace(csrfHeader) || !FixedTimeEquals(csrfCookie, csrfHeader))
        {
            error = "CSRF 校验失败";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool ValidateTrustedOrigin(HttpRequest request, out string error)
    {
        var origin = request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(origin))
            origin = NormalizeOrigin(request.Headers.Referer.FirstOrDefault());

        var normalizedOrigin = NormalizeOrigin(origin);
        if (normalizedOrigin == null || !_allowedOrigins.Contains(normalizedOrigin))
        {
            error = "请求来源不受信任";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public void WriteSessionCookies(HttpResponse response, string refreshToken, DateTime refreshExpiresAt)
    {
        response.Cookies.Append(_options.RefreshCookieName, refreshToken, BuildRefreshCookieOptions(refreshExpiresAt));
        response.Cookies.Append(_options.CsrfCookieName, CreateRandomToken(), new CookieOptions
        {
            HttpOnly = false,
            Secure = _options.CookieSecure,
            SameSite = _options.CookieSameSite,
            Path = _options.CookiePath,
            Domain = string.IsNullOrWhiteSpace(_options.CookieDomain) ? null : _options.CookieDomain,
            Expires = new DateTimeOffset(refreshExpiresAt),
            IsEssential = true
        });
    }

    public void ClearSessionCookies(HttpResponse response)
    {
        response.Cookies.Delete(_options.RefreshCookieName, BuildRefreshCookieOptions(DateTime.UnixEpoch));
        response.Cookies.Delete(_options.CsrfCookieName, new CookieOptions
        {
            HttpOnly = false,
            Secure = _options.CookieSecure,
            SameSite = _options.CookieSameSite,
            Path = _options.CookiePath,
            Domain = string.IsNullOrWhiteSpace(_options.CookieDomain) ? null : _options.CookieDomain,
            Expires = DateTimeOffset.UnixEpoch,
            IsEssential = true
        });
    }

    private CookieOptions BuildRefreshCookieOptions(DateTime expiresAt) => new()
    {
        HttpOnly = true,
        Secure = _options.CookieSecure,
        SameSite = _options.CookieSameSite,
        Path = _options.CookiePath,
        Domain = string.IsNullOrWhiteSpace(_options.CookieDomain) ? null : _options.CookieDomain,
        Expires = new DateTimeOffset(expiresAt),
        IsEssential = true
    };

    private static string CreateRandomToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string? NormalizeOrigin(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;
        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}
