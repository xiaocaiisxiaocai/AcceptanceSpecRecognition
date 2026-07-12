using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace AcceptanceSpecSystem.Api.Options;

public static class ProxyForwardingConfiguration
{
    private const int MaxForwardLimit = 5;

    public static ForwardedHeadersOptions Create(ProxyForwardingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
            throw new InvalidOperationException("ProxyForwarding 未显式启用");
        if (options.ForwardLimit is < 1 or > MaxForwardLimit)
            throw new InvalidOperationException($"ProxyForwarding:ForwardLimit 必须在 1 到 {MaxForwardLimit} 之间");

        var forwardedOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = options.ForwardLimit,
            RequireHeaderSymmetry = true
        };
        // 框架默认信任 loopback。显式代理模式只信任配置列出的来源，避免环境差异扩大信任面。
        forwardedOptions.KnownProxies.Clear();
        forwardedOptions.KnownNetworks.Clear();

        foreach (var value in Normalize(options.KnownProxies))
        {
            if (!IPAddress.TryParse(value, out var address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
                throw new InvalidOperationException($"ProxyForwarding:KnownProxies 包含无效或过宽地址：{value}");
            forwardedOptions.KnownProxies.Add(address);
        }

        foreach (var value in Normalize(options.KnownNetworks))
        {
            var separator = value.LastIndexOf('/');
            if (separator <= 0 || separator == value.Length - 1 ||
                !IPAddress.TryParse(value[..separator], out var prefix) ||
                !int.TryParse(value[(separator + 1)..], out var prefixLength))
                throw new InvalidOperationException($"ProxyForwarding:KnownNetworks 必须使用有效 CIDR：{value}");

            var maxPrefixLength = prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
            if (prefixLength is < 1 || prefixLength > maxPrefixLength)
                throw new InvalidOperationException($"ProxyForwarding:KnownNetworks 禁止全网信任或无效前缀：{value}");

#pragma warning disable CS0618 // .NET 8 ForwardedHeadersOptions 仍使用 Microsoft.AspNetCore.HttpOverrides.IPNetwork。
            forwardedOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength));
#pragma warning restore CS0618
        }

        if (forwardedOptions.KnownProxies.Count == 0 && forwardedOptions.KnownNetworks.Count == 0)
            throw new InvalidOperationException(
                "ProxyForwarding 已启用，但未配置 KnownProxies 或 KnownNetworks；禁止信任任意转发头");

        return forwardedOptions;
    }

    private static IEnumerable<string> Normalize(IEnumerable<string>? values) =>
        (values ?? [])
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase);
}
