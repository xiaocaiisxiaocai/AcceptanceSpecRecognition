using System.Security.Cryptography;
using System.Text;

namespace AcceptanceSpecSystem.Api.Options;

internal static class RateLimitPartitionKeyResolver
{
    public static string Resolve(HttpContext httpContext)
    {
        var partitionKey = httpContext.User?.Identity?.IsAuthenticated == true
            ? httpContext.User.Identity.Name
            : httpContext.Connection.RemoteIpAddress?.ToString();

        return string.IsNullOrWhiteSpace(partitionKey) ? "anonymous" : partitionKey;
    }

    public static string ResolveRefreshSession(HttpContext httpContext, string refreshCookieName)
    {
        var refreshToken = httpContext.Request.Cookies[refreshCookieName]?.Trim();
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
            return $"refresh-session:{digest}";
        }

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrWhiteSpace(ipAddress) ? "refresh-anonymous" : $"refresh-ip:{ipAddress}";
    }
}
