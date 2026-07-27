using System.Net;
using System.Net.Sockets;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

public interface IAiSocketConnector
{
    ValueTask<Stream> ConnectAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken);
}

public interface IAiSocketFactory
{
    Socket Create(AddressFamily addressFamily);
}

public sealed class AiSocketFactory : IAiSocketFactory
{
    public Socket Create(AddressFamily addressFamily) =>
        new(addressFamily, SocketType.Stream, ProtocolType.Tcp);
}

public sealed class AiSocketConnector : IAiSocketConnector
{
    private readonly IAiSocketFactory _socketFactory;

    public AiSocketConnector(IAiSocketFactory socketFactory)
    {
        _socketFactory = socketFactory;
    }

    public async ValueTask<Stream> ConnectAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        var socket = _socketFactory.Create(address.AddressFamily);
        try
        {
            await socket.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
