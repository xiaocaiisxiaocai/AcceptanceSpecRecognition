using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Core.Tests.AI.SemanticKernel;

[Collection("AI安全传输全局代理隔离")]
public class SafeAiHttpMessageHandlerFactoryTests
{
    [Fact]
    public async Task 安全客户端工厂_同提供商同Origin同策略代次应复用连接池()
    {
        var connector = new ReusableSocketConnector(
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok",
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
        using var factory = CreateFactory(
            new SequenceDnsResolver([IPAddress.Parse("10.20.1.7")]),
            connector);
        using var firstClient = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            "http://models.internal:8080/v1");
        using var secondClient = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            "http://MODELS.internal.:8080/other");

        using var first = await firstClient.GetAsync("http://models.internal:8080/v1/models");
        using var second = await secondClient.GetAsync("http://models.internal:8080/v1/embeddings");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        connector.Addresses.Should().ContainSingle("同一隔离键应共享连接池，而不是重复建连");
    }

    [Fact]
    public async Task 安全Handler_DNS重绑定为危险地址时不应把第二次地址交给Connector()
    {
        var resolver = new SequenceDnsResolver(
            [IPAddress.Parse("10.20.1.8")],
            [IPAddress.Parse("169.254.169.254")]);
        var connector = new ScriptedSocketConnector(
            SuccessResponse(),
            SuccessResponse());
        using var factory = CreateFactory(resolver, connector);
        using var client = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            "http://models.internal:8080");

        using var first = await client.GetAsync("http://models.internal:8080/v1/models");
        var second = () => client.GetAsync("http://models.internal:8080/v1/models");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        await second.Should().ThrowAsync<HttpRequestException>();
        connector.Addresses.Should().Equal(IPAddress.Parse("10.20.1.8"));
        resolver.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task 安全Handler_应把策略返回的具体IP原样交给Connector并保持原Host()
    {
        var connector = new ScriptedSocketConnector(SuccessResponse());
        using var factory = CreateFactory(
            new SequenceDnsResolver([IPAddress.Parse("10.20.1.9")]),
            connector);
        using var client = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            "http://模型.example:8080");

        using var response = await client.GetAsync("http://xn--xgs754b.example:8080/v1/models");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        connector.Addresses.Should().Equal(IPAddress.Parse("10.20.1.9"));
        connector.Streams.Should().ContainSingle();
        connector.Streams[0].WrittenText.Should().Contain("Host: xn--xgs754b.example:8080\r\n");
        connector.Streams[0].WrittenText.Should().Contain("GET /v1/models HTTP/");
    }

    [Fact]
    public void 安全Handler_应固定禁用代理和自动重定向并设置连接池边界()
    {
        using var factory = CreateFactory(
            new SequenceDnsResolver([IPAddress.Loopback]),
            new ScriptedSocketConnector(SuccessResponse()));

        var handler = factory.CreateHandler(
            AiServiceType.Ollama,
            new Uri("http://127.0.0.1:11434"),
            factory.Generation);
        var socketsHandler = FindSocketsHandler(handler);

        socketsHandler.AllowAutoRedirect.Should().BeFalse();
        socketsHandler.UseProxy.Should().BeFalse();
        socketsHandler.ConnectTimeout.Should().Be(TimeSpan.FromSeconds(10));
        socketsHandler.PooledConnectionLifetime.Should().Be(TimeSpan.FromMinutes(5));
        socketsHandler.PooledConnectionIdleTimeout.Should().Be(TimeSpan.FromMinutes(1));
        socketsHandler.SslOptions.RemoteCertificateValidationCallback.Should().BeNull();
        handler.Dispose();
    }

    [Fact]
    public async Task 安全Handler_存在全局代理时真实请求仍应直连策略批准的地址()
    {
        var originalProxy = WebRequest.DefaultWebProxy;
        try
        {
            WebRequest.DefaultWebProxy = new WebProxy("http://127.0.0.1:9");
            var connector = new ScriptedSocketConnector(SuccessResponse());
            using var factory = CreateFactory(
                new SequenceDnsResolver([IPAddress.Parse("10.20.1.15")]),
                connector);
            using var client = factory.CreateClient(
                AiServiceType.CustomOpenAICompatible,
                "http://models.internal:8080");

            using var response = await client.GetAsync("http://models.internal:8080/v1/models");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            connector.Addresses.Should().Equal(IPAddress.Parse("10.20.1.15"));
        }
        finally
        {
            WebRequest.DefaultWebProxy = originalProxy;
        }
    }

    [Fact]
    public async Task 安全Handler_收到302时应返回首跳响应且不连接第二跳()
    {
        var connector = new ScriptedSocketConnector(RedirectResponse("http://169.254.169.254/latest"));
        using var factory = CreateFactory(
            new SequenceDnsResolver([IPAddress.Parse("10.20.1.10")]),
            connector);
        using var client = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            "http://models.internal:8080");

        using var response = await client.GetAsync("http://models.internal:8080/v1/models");

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location.Should().Be("http://169.254.169.254/latest");
        connector.Addresses.Should().ContainSingle();
    }

    [Fact]
    public async Task 安全Handler_请求跨Origin时应在连接前拒绝()
    {
        var connector = new ScriptedSocketConnector(SuccessResponse());
        using var factory = CreateFactory(
            new SequenceDnsResolver([IPAddress.Parse("10.20.1.11")]),
            connector);
        using var client = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            "http://models.internal:8080");

        var action = () => client.GetAsync("http://other.internal:8080/v1/models");

        var exception = await action.Should().ThrowAsync<AiEndpointAccessException>();
        exception.Which.Category.Should().Be(AiEndpointAccessFailureCategory.RequestOriginMismatch);
        connector.Addresses.Should().BeEmpty();
    }

    [Fact]
    public async Task 安全Handler_显式覆盖Host时应在连接前拒绝()
    {
        var connector = new ScriptedSocketConnector(SuccessResponse());
        using var factory = CreateFactory(
            new SequenceDnsResolver([IPAddress.Parse("10.20.1.12")]),
            connector);
        using var client = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            "http://models.internal:8080");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "http://models.internal:8080/v1/models");
        request.Headers.Host = "169.254.169.254";

        var action = () => client.SendAsync(request);

        await action.Should().ThrowAsync<AiEndpointAccessException>()
            .Where(exception => exception.Category == AiEndpointAccessFailureCategory.RequestOriginMismatch);
        connector.Addresses.Should().BeEmpty();
    }

    [Fact]
    public async Task 安全Handler_Connector取消应保留取消语义()
    {
        using var cancellation = new CancellationTokenSource();
        var connector = new CancellingSocketConnector(cancellation);
        using var factory = CreateFactory(
            new SequenceDnsResolver([IPAddress.Parse("10.20.1.13")]),
            connector);
        using var client = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            "http://models.internal:8080");

        var action = () => client.GetAsync(
            "http://models.internal:8080/v1/models",
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task 安全Handler_Connector失败应返回稳定类别且不泄露IP和远端详情()
    {
        using var factory = CreateFactory(
            new SequenceDnsResolver([IPAddress.Parse("10.20.1.14")]),
            new FailingSocketConnector("10.20.1.14 api-key=secret"));
        using var client = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            "http://models.internal:8080");

        var action = () => client.GetAsync("http://models.internal:8080/v1/models");

        var exception = await action.Should().ThrowAsync<HttpRequestException>();
        var accessFailure = EnumerateExceptions(exception.Which)
            .OfType<AiEndpointAccessException>()
            .Single();
        accessFailure.Category.Should().Be(AiEndpointAccessFailureCategory.ConnectFailed);
        accessFailure.Message.Should().NotContain("10.20.1.14").And.NotContain("secret");
        exception.Which.ToString().Should().NotContain("10.20.1.14").And.NotContain("secret");
    }

    [Fact]
    public async Task 安全Handler_TLS应保持原主机SNI且错误证书必须失败()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest(
            "CN=wrong.example",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(5));
        string? observedSni = null;
        var serverTask = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync();
            await using var network = new NetworkStream(socket, ownsSocket: false);
            await using var tls = new SslStream(network);
            try
            {
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ServerCertificateSelectionCallback = (_, host) =>
                    {
                        observedSni = host;
                        return certificate;
                    }
                });
            }
            catch (AuthenticationException)
            {
            }
            catch (IOException)
            {
            }
        });
        var options = new AiEndpointSecurityOptions
        {
            PrivateNetworkAllowlist =
            [
                new AiEndpointPrivateNetworkRule { Cidr = "127.0.0.0/8", Ports = [port] }
            ]
        };
        using var policy = new AiEndpointAccessPolicy(
            new SequenceDnsResolver([IPAddress.Loopback]),
            new StaticOptionsMonitor<AiEndpointSecurityOptions>(options));
        using var factory = new SafeAiHttpMessageHandlerFactory(
            policy,
            new AiSocketConnector(new AiSocketFactory()));
        using var client = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            $"https://localhost:{port}");

        var action = () => client.GetAsync($"https://localhost:{port}/v1/models");

        await action.Should().ThrowAsync<HttpRequestException>();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        observedSni.Should().Be("localhost");
    }

    private static SafeAiHttpMessageHandlerFactory CreateFactory(
        IAiDnsResolver resolver,
        IAiSocketConnector connector)
    {
        var options = new AiEndpointSecurityOptions
        {
            PrivateNetworkAllowlist =
            [
                new AiEndpointPrivateNetworkRule
                {
                    Cidr = "10.20.0.0/16",
                    Ports = [8080]
                }
            ]
        };
        var policy = new AiEndpointAccessPolicy(
            resolver,
            new StaticOptionsMonitor<AiEndpointSecurityOptions>(options));
        return new SafeAiHttpMessageHandlerFactory(policy, connector);
    }

    private static SocketsHttpHandler FindSocketsHandler(HttpMessageHandler handler)
    {
        HttpMessageHandler? current = handler;
        while (current is DelegatingHandler delegating)
            current = delegating.InnerHandler;
        return current.Should().BeOfType<SocketsHttpHandler>().Subject;
    }

    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException!)
            yield return current;
    }

    private static string SuccessResponse() =>
        "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok";

    private static string RedirectResponse(string location) =>
        $"HTTP/1.1 302 Found\r\nLocation: {location}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";

    private sealed class SequenceDnsResolver(params IPAddress[][] results) : IAiDnsResolver
    {
        public int CallCount { get; private set; }

        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(CallCount, results.Length - 1);
            CallCount++;
            return ValueTask.FromResult<IReadOnlyList<IPAddress>>(results[index]);
        }
    }

    private sealed class ScriptedSocketConnector(params string[] responses) : IAiSocketConnector
    {
        public List<IPAddress> Addresses { get; } = [];

        public List<ScriptedDuplexStream> Streams { get; } = [];

        public ValueTask<Stream> ConnectAsync(
            IPAddress address,
            int port,
            CancellationToken cancellationToken)
        {
            Addresses.Add(address);
            var response = responses[Math.Min(Streams.Count, responses.Length - 1)];
            var stream = new ScriptedDuplexStream(response);
            Streams.Add(stream);
            return ValueTask.FromResult<Stream>(stream);
        }
    }

    private sealed class ReusableSocketConnector(params string[] responses) : IAiSocketConnector
    {
        public List<IPAddress> Addresses { get; } = [];

        public ValueTask<Stream> ConnectAsync(
            IPAddress address,
            int port,
            CancellationToken cancellationToken)
        {
            Addresses.Add(address);
            return ValueTask.FromResult<Stream>(new RequestDrivenDuplexStream(responses));
        }
    }

    private sealed class CancellingSocketConnector(CancellationTokenSource cancellation) : IAiSocketConnector
    {
        public ValueTask<Stream> ConnectAsync(
            IPAddress address,
            int port,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return ValueTask.FromCanceled<Stream>(cancellationToken);
        }
    }

    private sealed class FailingSocketConnector(string message) : IAiSocketConnector
    {
        public ValueTask<Stream> ConnectAsync(
            IPAddress address,
            int port,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromException<Stream>(new InvalidOperationException(message));
        }
    }

    private sealed class ScriptedDuplexStream(string response) : Stream
    {
        private readonly MemoryStream _reads = new(Encoding.ASCII.GetBytes(response));
        private readonly MemoryStream _writes = new();

        public string WrittenText => Encoding.ASCII.GetString(_writes.ToArray());

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) =>
            _reads.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _reads.ReadAsync(buffer, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) =>
            _writes.Write(buffer, offset, count);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _writes.WriteAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class RequestDrivenDuplexStream(params string[] responses) : Stream
    {
        private readonly object _gate = new();
        private readonly Queue<byte[]> _readyResponses = new();
        private readonly SemaphoreSlim _responseReady = new(0);
        private readonly MemoryStream _written = new();
        private int _responseIndex;
        private byte[]? _currentResponse;
        private int _currentOffset;
        private int _observedHeaderEnds;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            while (_currentResponse == null || _currentOffset >= _currentResponse.Length)
            {
                await _responseReady.WaitAsync(cancellationToken);
                lock (_gate)
                {
                    _currentResponse = _readyResponses.Dequeue();
                    _currentOffset = 0;
                }
            }

            var count = Math.Min(buffer.Length, _currentResponse.Length - _currentOffset);
            _currentResponse.AsMemory(_currentOffset, count).CopyTo(buffer);
            _currentOffset += count;
            return count;
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _written.Write(buffer.Span);
                var text = Encoding.ASCII.GetString(_written.ToArray());
                var headerEnds = CountOccurrences(text, "\r\n\r\n");
                while (_observedHeaderEnds < headerEnds && _responseIndex < responses.Length)
                {
                    _observedHeaderEnds++;
                    _readyResponses.Enqueue(Encoding.ASCII.GetBytes(responses[_responseIndex++]));
                    _responseReady.Release();
                }
            }

            return ValueTask.CompletedTask;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        private static int CountOccurrences(string value, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

[CollectionDefinition("AI安全传输全局代理隔离", DisableParallelization = true)]
public sealed class Ai安全传输全局代理隔离Collection;
