using System.Net.Http.Json;

namespace AcceptanceSpecSystem.Api.Tests.Infrastructure;

internal static class AuthCookieTestHelper
{
    internal const string RefreshCookieName = "__Host-acceptance-refresh";
    internal const string InsecureRefreshCookieName = "acceptance-refresh";
    internal const string CsrfCookieName = "acceptance-csrf";
    internal const string CsrfHeaderName = "X-CSRF-Token";
    internal const string AllowedOrigin = "http://localhost";

    internal static (string RefreshToken, string CsrfToken) ReadSessionCookies(HttpResponseMessage response) =>
        (ReadCookie(response, RefreshCookieName), ReadCookie(response, CsrfCookieName));

    internal static HttpRequestMessage CreateStateChangingRequest(
        string path,
        string refreshToken,
        string csrfToken,
        string origin = AllowedOrigin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("Origin", origin);
        request.Headers.Add(CsrfHeaderName, csrfToken);
        request.Headers.Add("Cookie", $"{RefreshCookieName}={refreshToken}; {CsrfCookieName}={csrfToken}");
        return request;
    }

    internal static HttpRequestMessage CreateLoginRequest(
        string username,
        string password,
        string? origin = AllowedOrigin,
        string? referer = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/login")
        {
            Content = JsonContent.Create(new { username, password })
        };
        if (!string.IsNullOrWhiteSpace(origin))
            request.Headers.Add("Origin", origin);
        if (!string.IsNullOrWhiteSpace(referer))
            request.Headers.Referrer = new Uri(referer);
        return request;
    }

    internal static string ReadCookie(HttpResponseMessage response, string name)
    {
        response.Headers.TryGetValues("Set-Cookie", out var values);
        var prefix = name + "=";
        var cookie = values?.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        if (cookie == null)
            throw new InvalidOperationException($"响应未写入 Cookie: {name}");

        return cookie[prefix.Length..].Split(';', 2)[0];
    }
}
