namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

public sealed class AiEndpointSecurityOptions
{
    public const string SectionName = "AiEndpointSecurity";

    public List<AiEndpointPrivateNetworkRule> PrivateNetworkAllowlist { get; set; } = [];
}

public sealed class AiEndpointPrivateNetworkRule
{
    public string Cidr { get; set; } = string.Empty;

    public List<int> Ports { get; set; } = [];
}
