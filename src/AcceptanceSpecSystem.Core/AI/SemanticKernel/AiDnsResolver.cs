using System.Net;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

public interface IAiDnsResolver
{
    ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken);
}

public sealed class AiDnsResolver : IAiDnsResolver
{
    public async ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken)
    {
        return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
    }
}
