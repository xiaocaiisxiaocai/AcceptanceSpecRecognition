namespace AcceptanceSpecSystem.Api.Options;

public sealed class ProxyForwardingOptions
{
    public const string SectionName = "ProxyForwarding";

    public bool Enabled { get; set; }

    public int ForwardLimit { get; set; } = 1;

    public string[] KnownProxies { get; set; } = [];

    public string[] KnownNetworks { get; set; } = [];
}
