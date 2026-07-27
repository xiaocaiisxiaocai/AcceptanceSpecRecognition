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

public interface IAiSocketConnectOperation
{
    ValueTask ConnectAsync(
        Socket socket,
        IPAddress address,
        int port,
        CancellationToken cancellationToken);
}

public sealed class AiSocketFactory : IAiSocketFactory
{
    public Socket Create(AddressFamily addressFamily) =>
        new(addressFamily, SocketType.Stream, ProtocolType.Tcp);
}

public sealed class AiSocketConnectOperation : IAiSocketConnectOperation
{
    public ValueTask ConnectAsync(
        Socket socket,
        IPAddress address,
        int port,
        CancellationToken cancellationToken) =>
        socket.ConnectAsync(address, port, cancellationToken);
}

public sealed class AiSocketConnector : IAiSocketConnector
{
    private readonly IAiSocketFactory _socketFactory;
    private readonly IAiSocketConnectOperation _connectOperation;

    public AiSocketConnector(
        IAiSocketFactory socketFactory,
        IAiSocketConnectOperation connectOperation)
    {
        _socketFactory = socketFactory;
        _connectOperation = connectOperation;
    }

    public async ValueTask<Stream> ConnectAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        var socket = _socketFactory.Create(address.AddressFamily);
        try
        {
            await _connectOperation.ConnectAsync(
                socket,
                address,
                port,
                cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
