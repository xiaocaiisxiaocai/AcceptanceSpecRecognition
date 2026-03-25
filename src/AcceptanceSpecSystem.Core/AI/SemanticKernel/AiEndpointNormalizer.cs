namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

/// <summary>
/// AI 服务 Endpoint 规范化工具。
/// 统一处理常见输入错误，例如将 `http:127.0.0.1:11434` 修正为 `http://127.0.0.1:11434`。
/// </summary>
public static class AiEndpointNormalizer
{
    public static string? NormalizeOptionalEndpoint(string? endpoint)
    {
        return string.IsNullOrWhiteSpace(endpoint)
            ? null
            : NormalizeRequiredEndpoint(endpoint);
    }

    public static string NormalizeRequiredEndpoint(string? endpoint, string fieldName = "Endpoint")
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException($"{fieldName} 未配置");

        var value = endpoint.Trim();
        if (value.StartsWith("http:", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            value = "http://" + value["http:".Length..].TrimStart('/');
        }
        else if (value.StartsWith("https:", StringComparison.OrdinalIgnoreCase) &&
                 !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = "https://" + value["https:".Length..].TrimStart('/');
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"{fieldName} 必须是有效的 http/https 绝对地址");
        }

        return uri.ToString().TrimEnd('/');
    }
}
