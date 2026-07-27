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
        var connector = new AiSocketConnector(
            new SingleSocketFactory(socket),
            new FailingConnectOperation(new SocketException((int)SocketError.ConnectionRefused)));

        var action = () => connector.ConnectAsync(
            IPAddress.Loopback,
            443,
            CancellationToken.None).AsTask();

        await action.Should().ThrowAsync<SocketException>();
        socket.SafeHandle.IsClosed.Should().BeTrue();
    }

    [Fact]
    public async Task SocketConnector_连接取消时应原样传播并释放已创建Socket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var connector = new AiSocketConnector(
            new SingleSocketFactory(socket),
            new CancellingConnectOperation());

        var action = () => connector.ConnectAsync(
            IPAddress.Parse("192.0.2.1"),
            443,
            cancellation.Token).AsTask();

        var exception = await action.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellation.Token);
        socket.SafeHandle.IsClosed.Should().BeTrue();
    }

    private sealed class FailingConnectOperation(Exception exception) : IAiSocketConnectOperation
    {
        public ValueTask ConnectAsync(
            Socket socket,
            IPAddress address,
            int port,
            CancellationToken cancellationToken) =>
            ValueTask.FromException(exception);
    }

    private sealed class CancellingConnectOperation : IAiSocketConnectOperation
    {
        public ValueTask ConnectAsync(
            Socket socket,
            IPAddress address,
            int port,
            CancellationToken cancellationToken) =>
            ValueTask.FromCanceled(cancellationToken);
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
