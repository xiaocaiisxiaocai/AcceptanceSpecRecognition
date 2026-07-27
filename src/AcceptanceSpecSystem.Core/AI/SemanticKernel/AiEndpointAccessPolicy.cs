using System.Net;
using System.Net.Sockets;
using AcceptanceSpecSystem.Core.AI.Models;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

public enum AiEndpointAccessFailureCategory
{
    AddressBlocked,
    DnsFailed,
    PolicyChanged,
    ConnectFailed,
    RequestOriginMismatch
}

public sealed class AiEndpointAccessException : InvalidOperationException
{
    public AiEndpointAccessException(
        AiEndpointAccessFailureCategory category,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Category = category;
    }

    public AiEndpointAccessFailureCategory Category { get; }
}

public sealed record AiEndpointResolution(
    long Generation,
    IReadOnlyList<IPAddress> Addresses);

public interface IAiEndpointAccessPolicy
{
    long Generation { get; }

    ValueTask<AiEndpointResolution> ValidateAsync(
        Uri endpoint,
        AiServiceType serviceType,
        CancellationToken cancellationToken);

    ValueTask<AiEndpointResolution> ValidateAsync(
        Uri endpoint,
        AiServiceType serviceType,
        long expectedGeneration,
        CancellationToken cancellationToken);
}

public sealed class AiEndpointAccessPolicy : IAiEndpointAccessPolicy, IDisposable
{
    private static readonly IPAddress[] MetadataAddresses =
    [
        IPAddress.Parse("169.254.169.254"),
        IPAddress.Parse("169.254.170.2"),
        IPAddress.Parse("100.100.100.200"),
        IPAddress.Parse("fd00:ec2::254")
    ];

    private readonly IAiDnsResolver _resolver;
    private readonly IDisposable? _changeRegistration;
    private PolicySnapshot _snapshot;
    private long _generation = 1;

    public AiEndpointAccessPolicy(
        IAiDnsResolver resolver,
        IOptionsMonitor<AiEndpointSecurityOptions> options)
    {
        _resolver = resolver;
        _snapshot = PolicySnapshot.Create(options.CurrentValue);
        _changeRegistration = options.OnChange((next, _) =>
        {
            Volatile.Write(ref _snapshot, PolicySnapshot.Create(next));
            Interlocked.Increment(ref _generation);
        });
    }

    public long Generation => Volatile.Read(ref _generation);

    public ValueTask<AiEndpointResolution> ValidateAsync(
        Uri endpoint,
        AiServiceType serviceType,
        CancellationToken cancellationToken)
    {
        return ValidateAsync(endpoint, serviceType, Generation, cancellationToken);
    }

    public async ValueTask<AiEndpointResolution> ValidateAsync(
        Uri endpoint,
        AiServiceType serviceType,
        long expectedGeneration,
        CancellationToken cancellationToken)
    {
        if (expectedGeneration != Generation)
            throw PolicyChanged();

        var host = endpoint.IdnHost.TrimEnd('.');
        if (IsMetadataHost(host))
            throw AddressBlocked();

        IReadOnlyList<IPAddress> resolved;
        if (IPAddress.TryParse(host, out var literal))
        {
            resolved = [NormalizeAddress(literal)];
        }
        else
        {
            try
            {
                resolved = await _resolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw new AiEndpointAccessException(
                    AiEndpointAccessFailureCategory.DnsFailed,
                    "AI 端点 DNS 解析失败");
            }

            if (resolved.Count == 0)
            {
                throw new AiEndpointAccessException(
                    AiEndpointAccessFailureCategory.DnsFailed,
                    "AI 端点 DNS 解析失败");
            }

            resolved = resolved
                .Select(NormalizeAddress)
                .Distinct()
                .ToArray();
        }

        if (expectedGeneration != Generation)
            throw PolicyChanged();

        var snapshot = Volatile.Read(ref _snapshot);
        var port = endpoint.Port;
        foreach (var address in resolved)
        {
            if (!IsAddressAllowed(address, endpoint.Scheme, port, serviceType, snapshot))
                throw AddressBlocked();
        }

        return new AiEndpointResolution(expectedGeneration, resolved);
    }

    public void Dispose()
    {
        _changeRegistration?.Dispose();
    }

    private static bool IsAddressAllowed(
        IPAddress address,
        string scheme,
        int port,
        AiServiceType serviceType,
        PolicySnapshot snapshot)
    {
        if (IsNeverAllowed(address))
            return false;

        if (IsGlobalUnicast(address))
            return string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

        if (IPAddress.IsLoopback(address) &&
            ((serviceType == AiServiceType.Ollama && port == 11434) ||
             (serviceType == AiServiceType.LMStudio && port == 1234)))
        {
            return true;
        }

        if (!SupportsExplicitPrivateNetwork(serviceType) ||
            !IsPrivateAllowlistCandidate(address))
        {
            return false;
        }

        return snapshot.Rules.Any(rule => rule.Ports.Contains(port) && rule.Network.Contains(address));
    }

    private static bool SupportsExplicitPrivateNetwork(AiServiceType serviceType) =>
        serviceType is AiServiceType.Ollama
            or AiServiceType.LMStudio
            or AiServiceType.CustomOpenAICompatible;

    private static bool IsPrivateAllowlistCandidate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return (bytes[0] & 0xFE) == 0xFC;
        }

        return false;
    }

    private static bool IsNeverAllowed(IPAddress address)
    {
        if (MetadataAddresses.Contains(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 0 ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   bytes[0] >= 224;
        }

        return address.Equals(IPAddress.IPv6None) ||
               address.IsIPv6LinkLocal ||
               address.IsIPv6Multicast ||
               address.IsIPv6SiteLocal;
    }

    private static bool IsGlobalUnicast(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var ipv4Bytes = address.GetAddressBytes();
            return ipv4Bytes[0] != 0 &&
                   ipv4Bytes[0] != 10 &&
                   ipv4Bytes[0] != 127 &&
                   !(ipv4Bytes[0] == 100 && ipv4Bytes[1] is >= 64 and <= 127) &&
                   !(ipv4Bytes[0] == 169 && ipv4Bytes[1] == 254) &&
                   !(ipv4Bytes[0] == 172 && ipv4Bytes[1] is >= 16 and <= 31) &&
                   !(ipv4Bytes[0] == 192 && ipv4Bytes[1] == 168) &&
                   !(ipv4Bytes[0] == 192 && ipv4Bytes[1] == 0 && ipv4Bytes[2] is 0 or 2) &&
                   !(ipv4Bytes[0] == 198 && ipv4Bytes[1] is 18 or 19) &&
                   !(ipv4Bytes[0] == 198 && ipv4Bytes[1] == 51 && ipv4Bytes[2] == 100) &&
                   !(ipv4Bytes[0] == 203 && ipv4Bytes[1] == 0 && ipv4Bytes[2] == 113) &&
                   ipv4Bytes[0] < 224;
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6 ||
            address.Equals(IPAddress.IPv6None) ||
            IPAddress.IsLoopback(address) ||
            address.IsIPv6LinkLocal ||
            address.IsIPv6Multicast ||
            address.IsIPv6SiteLocal)
        {
            return false;
        }

        var ipv6Bytes = address.GetAddressBytes();
        return (ipv6Bytes[0] & 0xFE) != 0xFC &&
               !IsInPrefix(ipv6Bytes, IPAddress.Parse("100::").GetAddressBytes(), 64) &&
               !IsInPrefix(ipv6Bytes, IPAddress.Parse("2001::").GetAddressBytes(), 32) &&
               !IsInPrefix(ipv6Bytes, IPAddress.Parse("2001:2::").GetAddressBytes(), 48) &&
               !IsInPrefix(ipv6Bytes, IPAddress.Parse("2001:db8::").GetAddressBytes(), 32) &&
               !IsInPrefix(ipv6Bytes, IPAddress.Parse("2001:20::").GetAddressBytes(), 28) &&
               !IsInPrefix(ipv6Bytes, IPAddress.Parse("2002::").GetAddressBytes(), 16) &&
               !IsInPrefix(ipv6Bytes, IPAddress.Parse("64:ff9b::").GetAddressBytes(), 96) &&
               !IsInPrefix(ipv6Bytes, IPAddress.Parse("64:ff9b:1::").GetAddressBytes(), 48);
    }

    private static IPAddress NormalizeAddress(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static bool IsMetadataHost(string host)
    {
        return host.Equals("metadata", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("ec2metadata", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("metadata.azure.internal", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("ec2metadata.us-east-1.amazonaws.com", StringComparison.OrdinalIgnoreCase);
    }

    private static AiEndpointAccessException AddressBlocked() =>
        new(AiEndpointAccessFailureCategory.AddressBlocked, "AI 端点地址策略拒绝");

    private static AiEndpointAccessException PolicyChanged() =>
        new(AiEndpointAccessFailureCategory.PolicyChanged, "AI 端点访问策略已变化");

    private static bool IsInPrefix(byte[] address, byte[] network, int prefixLength)
    {
        var wholeBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;
        for (var index = 0; index < wholeBytes; index++)
        {
            if (address[index] != network[index])
                return false;
        }

        if (remainingBits == 0)
            return true;

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (address[wholeBytes] & mask) == (network[wholeBytes] & mask);
    }

    private sealed record PolicySnapshot(IReadOnlyList<PrivateNetworkRule> Rules)
    {
        public static PolicySnapshot Create(AiEndpointSecurityOptions options)
        {
            var rules = new List<PrivateNetworkRule>();
            foreach (var rule in options.PrivateNetworkAllowlist)
            {
                if (!IpNetwork.TryParse(rule.Cidr, out var network))
                    continue;

                var ports = rule.Ports
                    .Where(static port => port is >= 1 and <= 65535)
                    .ToHashSet();
                if (ports.Count > 0)
                    rules.Add(new PrivateNetworkRule(network, ports));
            }

            return new PolicySnapshot(rules);
        }
    }

    private sealed record PrivateNetworkRule(IpNetwork Network, IReadOnlySet<int> Ports);

    private sealed record IpNetwork(IPAddress Address, int PrefixLength)
    {
        public static bool TryParse(string value, out IpNetwork network)
        {
            network = null!;
            var parts = value.Split('/', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                !IPAddress.TryParse(parts[0], out var address) ||
                !int.TryParse(parts[1], out var prefixLength))
            {
                return false;
            }

            address = NormalizeAddress(address);
            var maxPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            if (prefixLength < 0 || prefixLength > maxPrefix)
                return false;

            network = new IpNetwork(address, prefixLength);
            return true;
        }

        public bool Contains(IPAddress candidate)
        {
            candidate = NormalizeAddress(candidate);
            if (candidate.AddressFamily != Address.AddressFamily)
                return false;

            return IsInPrefix(candidate.GetAddressBytes(), Address.GetAddressBytes(), PrefixLength);
        }
    }
}
