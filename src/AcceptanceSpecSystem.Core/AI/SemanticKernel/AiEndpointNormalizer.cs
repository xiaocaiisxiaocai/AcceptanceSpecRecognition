using System.Net;
using System.Net.Sockets;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

/// <summary>
/// AI 服务 Endpoint 规范化工具。
/// 统一处理常见输入错误，例如将 `http:127.0.0.1:11434` 修正为 `http://127.0.0.1:11434`。
/// </summary>
public static class AiEndpointNormalizer
{
    public static string? NormalizeOptionalEndpoint(
        string? endpoint,
        string fieldName = "Endpoint",
        bool allowPrivateNetwork = false)
    {
        return string.IsNullOrWhiteSpace(endpoint)
            ? null
            : NormalizeRequiredEndpoint(endpoint, fieldName, allowPrivateNetwork);
    }

    public static string NormalizeRequiredEndpoint(
        string? endpoint,
        string fieldName = "Endpoint",
        bool allowPrivateNetwork = false)
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

        if (!allowPrivateNetwork)
        {
            EnsureEndpointAllowed(uri, fieldName);
        }

        return uri.ToString().TrimEnd('/');
    }

    private static void EnsureEndpointAllowed(Uri uri, string fieldName)
    {
        var host = uri.DnsSafeHost.TrimEnd('.');
        if (string.IsNullOrEmpty(host))
        {
            throw new InvalidOperationException($"{fieldName} 无法解析主机名");
        }

        if (IsBlockedHostName(host))
        {
            throw new InvalidOperationException($"{fieldName} 不允许使用本地或内网地址");
        }

        if (uri.HostNameType == UriHostNameType.IPv6 || uri.HostNameType == UriHostNameType.IPv4)
        {
            if (!IPAddress.TryParse(host, out var address))
            {
                throw new InvalidOperationException($"{fieldName} 主机名非法");
            }

            if (IsPrivateOrLoopback(address))
            {
                throw new InvalidOperationException($"{fieldName} 不允许使用本地或内网地址");
            }
        }
        else
        {
            IPAddress[] addresses;
            try
            {
                addresses = Dns.GetHostAddresses(host);
            }
            catch (SocketException ex)
            {
                throw new InvalidOperationException($"{fieldName} 主机名无法解析", ex);
            }

            if (addresses.Length == 0)
            {
                throw new InvalidOperationException($"{fieldName} 主机名无法解析");
            }

            if (addresses.Any(IsPrivateOrLoopback))
            {
                throw new InvalidOperationException($"{fieldName} 不允许解析到本地或内网地址");
            }
        }
    }

    private static bool IsBlockedHostName(string host)
    {
        var normalized = host.Trim().ToLowerInvariant();
        if (normalized == "localhost" ||
            normalized == "localhost.localdomain" ||
            normalized == "ip6-localhost" ||
            normalized == "ip6-loopback" ||
            normalized == "0.0.0.0")
        {
            return true;
        }

        return normalized switch
        {
            "metadata.google.internal" => true,
            "metadata" => true,
            "ec2metadata" => true,
            "ec2metadata.us-east-1.amazonaws.com" => true,
            "metadata.azure.internal" => true,
            _ => false
        };
    }

    private static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            if (bytes[0] == 10)
                return true;
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                return true;
            if (bytes[0] == 0)
                return true;
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
                return true;

            var bytes = address.GetAddressBytes();
            if ((bytes[0] & 0xFE) == 0xFC)
                return true;
            var allZero = true;
            foreach (var b in bytes)
            {
                if (b != 0)
                {
                    allZero = false;
                    break;
                }
            }

            if (allZero)
                return true;
        }

        return false;
    }
}
