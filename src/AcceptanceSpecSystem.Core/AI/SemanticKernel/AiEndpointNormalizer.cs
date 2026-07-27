namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

/// <summary>
/// AI 服务 Endpoint URI 规范化工具。这里只校验 URI 结构，不解析 DNS 或区分地址类别。
/// </summary>
public static class AiEndpointNormalizer
{
    public static string? NormalizeOptionalEndpoint(
        string? endpoint,
        string fieldName = "Endpoint")
    {
        return string.IsNullOrWhiteSpace(endpoint)
            ? null
            : NormalizeRequiredEndpoint(endpoint, fieldName);
    }

    public static string NormalizeRequiredEndpoint(
        string? endpoint,
        string fieldName = "Endpoint")
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException($"{fieldName} 未配置");

        var value = endpoint.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !value.StartsWith($"{uri.Scheme}://", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.Port == 0)
        {
            throw new InvalidOperationException($"{fieldName} 必须是有效的 http/https 绝对地址");
        }

        string host;
        try
        {
            host = uri.IdnHost.TrimEnd('.');
        }
        catch (UriFormatException)
        {
            throw new InvalidOperationException($"{fieldName} 必须是有效的 http/https 绝对地址");
        }

        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException($"{fieldName} 必须是有效的 http/https 绝对地址");

        var normalized = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = host,
            Port = uri.IsDefaultPort ? -1 : uri.Port,
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri.AbsoluteUri;

        return normalized.TrimEnd('/');
    }
}
