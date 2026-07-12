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
}
