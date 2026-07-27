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

    void EnsureCurrent(long expectedGeneration);

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

internal interface IAiEndpointPolicyPublicationHook
{
    void RulesPreparedBeforePublish(long nextGeneration);
}

public sealed class AiEndpointAccessPolicy : IAiEndpointAccessPolicy, IDisposable
{
    // IANA IPv4/IPv6 Special-Purpose Address Registries, last reviewed 2026-07-27.
    // Longest-prefix matching is required because both registries contain globally
    // reachable anycast exceptions inside broader non-global protocol blocks.
    private static readonly SpecialPurposeRange[] Ipv4SpecialPurposeRanges =
    [
        Range("0.0.0.0/8", false),
        Range("10.0.0.0/8", false),
        Range("100.64.0.0/10", false),
        Range("127.0.0.0/8", false),
        Range("169.254.0.0/16", false),
        Range("172.16.0.0/12", false),
        Range("192.0.0.0/24", false),
        Range("192.0.0.9/32", true),
        Range("192.0.0.10/32", true),
        Range("192.0.2.0/24", false),
        Range("192.88.99.0/24", false),
        Range("192.168.0.0/16", false),
        Range("198.18.0.0/15", false),
        Range("198.51.100.0/24", false),
        Range("203.0.113.0/24", false),
        Range("224.0.0.0/4", false),
        Range("240.0.0.0/4", false)
    ];

    private static readonly SpecialPurposeRange[] Ipv6SpecialPurposeRanges =
    [
        Range("2001::/23", false),
        Range("2001::/32", false),
        Range("2001:1::1/128", true),
        Range("2001:1::2/128", true),
        Range("2001:1::3/128", true),
        Range("2001:2::/48", false),
        Range("2001:3::/32", true),
        Range("2001:4:112::/48", true),
        Range("2001:10::/28", false),
        Range("2001:20::/28", true),
        Range("2001:30::/28", true),
        Range("2001:db8::/32", false),
        Range("2002::/16", false),
        Range("3fff::/20", false)
    ];

    // IANA IPv6 Global Unicast Address Space allocation registry, reviewed 2026-07-27.
    // Unlisted portions of 2000::/3 are RESERVED and therefore fail closed.
    private static readonly IpNetwork[] Ipv6AllocatedGlobalUnicastRanges =
    [
        IpNetwork.Parse("2001::/23"),
        IpNetwork.Parse("2001:200::/23"),
        IpNetwork.Parse("2001:400::/23"),
        IpNetwork.Parse("2001:600::/23"),
        IpNetwork.Parse("2001:800::/22"),
        IpNetwork.Parse("2001:c00::/23"),
        IpNetwork.Parse("2001:e00::/23"),
        IpNetwork.Parse("2001:1200::/23"),
        IpNetwork.Parse("2001:1400::/22"),
        IpNetwork.Parse("2001:1800::/23"),
        IpNetwork.Parse("2001:1a00::/23"),
        IpNetwork.Parse("2001:1c00::/22"),
        IpNetwork.Parse("2001:2000::/19"),
        IpNetwork.Parse("2001:4000::/23"),
        IpNetwork.Parse("2001:4200::/23"),
        IpNetwork.Parse("2001:4400::/23"),
        IpNetwork.Parse("2001:4600::/23"),
        IpNetwork.Parse("2001:4800::/23"),
        IpNetwork.Parse("2001:4a00::/23"),
        IpNetwork.Parse("2001:4c00::/23"),
        IpNetwork.Parse("2001:5000::/20"),
        IpNetwork.Parse("2001:8000::/19"),
        IpNetwork.Parse("2001:a000::/20"),
        IpNetwork.Parse("2001:b000::/20"),
        IpNetwork.Parse("2002::/16"),
        IpNetwork.Parse("2003::/18"),
        IpNetwork.Parse("2400::/12"),
        IpNetwork.Parse("2410::/12"),
        IpNetwork.Parse("2600::/12"),
        IpNetwork.Parse("2610::/23"),
        IpNetwork.Parse("2620::/23"),
        IpNetwork.Parse("2630::/12"),
        IpNetwork.Parse("2800::/12"),
        IpNetwork.Parse("2a00::/12"),
        IpNetwork.Parse("2a10::/12"),
        IpNetwork.Parse("2c00::/12")
    ];

    private static readonly IPAddress[] MetadataAddresses =
    [
        IPAddress.Parse("169.254.169.254"),
        IPAddress.Parse("169.254.170.2"),
        IPAddress.Parse("100.100.100.200"),
        IPAddress.Parse("fd00:ec2::254")
    ];

    private readonly IAiDnsResolver _resolver;
    private readonly IOptionsMonitor<AiEndpointSecurityOptions> _options;
    private readonly IAiEndpointPolicyPublicationHook? _publicationHook;
    private readonly IDisposable? _changeRegistration;
    private PolicySnapshot _snapshot;

    public AiEndpointAccessPolicy(
        IAiDnsResolver resolver,
        IOptionsMonitor<AiEndpointSecurityOptions> options)
        : this(resolver, options, null)
    {
    }

    internal AiEndpointAccessPolicy(
        IAiDnsResolver resolver,
        IOptionsMonitor<AiEndpointSecurityOptions> options,
        IAiEndpointPolicyPublicationHook? publicationHook)
    {
        _resolver = resolver;
        _options = options;
        _publicationHook = publicationHook;
        _snapshot = PolicySnapshot.Create(options.CurrentValue, generation: 1);
        _changeRegistration = options.OnChange((_, _) => PublishLatestPolicy());
    }

    public long Generation => Volatile.Read(ref _snapshot).Generation;

    public void EnsureCurrent(long expectedGeneration)
    {
        if (Volatile.Read(ref _snapshot).Generation != expectedGeneration)
            throw PolicyChanged();
    }

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
        var snapshot = Volatile.Read(ref _snapshot);
        if (expectedGeneration != snapshot.Generation)
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

        EnsureSnapshotCurrent(snapshot);
        var port = endpoint.Port;
        foreach (var address in resolved)
        {
            if (!IsAddressAllowed(address, endpoint.Scheme, port, serviceType, snapshot))
                throw AddressBlocked();
        }

        EnsureSnapshotCurrent(snapshot);
        return new AiEndpointResolution(expectedGeneration, resolved);
    }

    public void Dispose()
    {
        _changeRegistration?.Dispose();
    }

    private void PublishLatestPolicy()
    {
        while (true)
        {
            var current = Volatile.Read(ref _snapshot);
            var next = PolicySnapshot.Create(
                _options.CurrentValue,
                checked(current.Generation + 1));
            _publicationHook?.RulesPreparedBeforePublish(next.Generation);
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _snapshot, next, current),
                    current))
            {
                return;
            }
        }
    }

    private void EnsureSnapshotCurrent(PolicySnapshot snapshot)
    {
        if (!ReferenceEquals(Volatile.Read(ref _snapshot), snapshot))
            throw PolicyChanged();
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
            return IsGloballyReachableByRegistry(address, Ipv4SpecialPurposeRanges);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6 ||
            !Ipv6AllocatedGlobalUnicastRanges.Any(range => range.Contains(address)))
        {
            return false;
        }

        return IsGloballyReachableByRegistry(address, Ipv6SpecialPurposeRanges);
    }

    private static bool IsGloballyReachableByRegistry(
        IPAddress address,
        IReadOnlyList<SpecialPurposeRange> ranges)
    {
        SpecialPurposeRange? match = null;
        foreach (var range in ranges)
        {
            if (range.Network.Contains(address) &&
                (match == null || range.Network.PrefixLength > match.Network.PrefixLength))
            {
                match = range;
            }
        }

        return match?.GloballyReachable ?? true;
    }

    private static SpecialPurposeRange Range(string cidr, bool globallyReachable) =>
        new(IpNetwork.Parse(cidr), globallyReachable);

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

    private sealed record PolicySnapshot(
        long Generation,
        IReadOnlyList<PrivateNetworkRule> Rules)
    {
        public static PolicySnapshot Create(
            AiEndpointSecurityOptions options,
            long generation)
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

            return new PolicySnapshot(generation, rules);
        }
    }

    private sealed record PrivateNetworkRule(IpNetwork Network, IReadOnlySet<int> Ports);

    private sealed record SpecialPurposeRange(IpNetwork Network, bool GloballyReachable);

    private sealed record IpNetwork(IPAddress Address, int PrefixLength)
    {
        public static IpNetwork Parse(string value) =>
            TryParse(value, out var network)
                ? network
                : throw new InvalidOperationException($"无效的内置网络范围: {value}");

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
