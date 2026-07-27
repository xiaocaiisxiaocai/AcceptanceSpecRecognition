using System.Net;
using System.Net.Sockets;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests.AI.SemanticKernel;

public class AiSocketConnectorTests
{
    [Fact]
    public async Task SocketConnector_连接失败时应释放已创建Socket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var connector = new AiSocketConnector(new SingleSocketFactory(socket));
        var port = GetUnusedPort();

        var action = () => connector.ConnectAsync(
            IPAddress.Loopback,
            port,
            CancellationToken.None).AsTask();

        await action.Should().ThrowAsync<SocketException>();
        socket.SafeHandle.IsClosed.Should().BeTrue();
    }

    [Fact]
    public async Task SocketConnector_连接取消时应原样传播并释放已创建Socket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var connector = new AiSocketConnector(new SingleSocketFactory(socket));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => connector.ConnectAsync(
            IPAddress.Parse("192.0.2.1"),
            443,
            cancellation.Token).AsTask();

        var exception = await action.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellation.Token);
        socket.SafeHandle.IsClosed.Should().BeTrue();
    }

    private static int GetUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class SingleSocketFactory(Socket socket) : IAiSocketFactory
    {
        public Socket Create(AddressFamily addressFamily)
        {
            socket.AddressFamily.Should().Be(addressFamily);
            return socket;
        }
    }
}
