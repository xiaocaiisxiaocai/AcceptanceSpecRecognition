using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests.AI.SemanticKernel;

[Collection("AI安全传输全局代理隔离")]
public class SafeAiHttpMessageHandlerFactoryTests
{
    [Theory]
    [InlineData("http://models.example:8080/v1")]
    [InlineData("http://models.internal:8080/v1")]
    [InlineData("http://8.8.8.8:8080/v1")]
    [InlineData("http://[2001:4860:4860::8888]:8080/v1")]
    public async Task 安全Handler_结构合法且请求同Origin时不应按公网私网或IP版本区别拒绝(
        string endpoint)
    {
        var transport = new RecordingHandler();
        using var handler = new ExactOriginGuardHandler(new Uri(endpoint), transport);
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}/models");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task 安全Handler_同源判定应按Scheme主机和有效端口精确比较()
    {
        var transport = new RecordingHandler();
        using var handler = new ExactOriginGuardHandler(
            new Uri("https://模型.example:443/v1"),
            transport);
        using var invoker = new HttpMessageInvoker(handler);

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://xn--xgs754b.example./models"),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("http://models.example:8443/v1/models")]
    [InlineData("https://other.example:443/v1/models")]
    [InlineData("https://models.example:444/v1/models")]
    public async Task 安全Handler_请求跨Origin时应在发送前拒绝(string requestUri)
    {
        var transport = new RecordingHandler();
        using var handler = new ExactOriginGuardHandler(
            new Uri("https://models.example:443/v1"),
            transport);
        using var invoker = new HttpMessageInvoker(handler);

        var action = () => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, requestUri),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<AiEndpointAccessException>();
        exception.Which.Category.Should().Be(AiEndpointAccessFailureCategory.RequestOriginMismatch);
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task 安全Handler_显式覆盖Host时应在发送前拒绝()
    {
        var transport = new RecordingHandler();
        using var handler = new ExactOriginGuardHandler(
            new Uri("https://models.example/v1"),
            transport);
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://models.example/v1/models");
        request.Headers.Host = "other.example";

        var action = () => invoker.SendAsync(request, CancellationToken.None);

        await action.Should().ThrowAsync<AiEndpointAccessException>()
            .Where(exception =>
                exception.Category == AiEndpointAccessFailureCategory.RequestOriginMismatch);
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task 安全Handler_同步请求跨Origin时应在服务器前拒绝()
    {
        var port = GetFreeTcpPort();
        using var listener = StartListener(port);
        var serverTask = ObserveSingleRequestAsync(listener);
        using var factory = new SafeAiHttpMessageHandlerFactory();
        using var client = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            $"http://localhost:{port}");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{port}/models");

        var exception = Record.Exception(() =>
        {
            using var response = client.Send(request);
        });
        listener.Stop();
        var serverReceivedRequest = await serverTask;

        exception.Should().BeOfType<AiEndpointAccessException>()
            .Which.Category.Should().Be(AiEndpointAccessFailureCategory.RequestOriginMismatch);
        serverReceivedRequest.Should().BeFalse();
    }

    [Fact]
    public async Task 安全Handler_同步请求覆盖Host时应在服务器前拒绝()
    {
        var port = GetFreeTcpPort();
        using var listener = StartListener(port);
        var serverTask = ObserveSingleRequestAsync(listener);
        using var factory = new SafeAiHttpMessageHandlerFactory();
        using var client = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            $"http://127.0.0.1:{port}");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{port}/models");
        request.Headers.Host = "other.example";

        var exception = Record.Exception(() =>
        {
            using var response = client.Send(request);
        });
        listener.Stop();
        var serverReceivedRequest = await serverTask;

        exception.Should().BeOfType<AiEndpointAccessException>()
            .Which.Category.Should().Be(AiEndpointAccessFailureCategory.RequestOriginMismatch);
        serverReceivedRequest.Should().BeFalse();
    }

    [Fact]
    public void 安全Handler_应固定禁用代理和自动重定向并使用系统TLS()
    {
        using var handler = SafeAiHttpMessageHandlerFactory.CreateHandler(
            new Uri("https://models.example/v1"));
        var socketsHandler = FindSocketsHandler(handler);

        socketsHandler.AllowAutoRedirect.Should().BeFalse();
        socketsHandler.UseProxy.Should().BeFalse();
        socketsHandler.ConnectCallback.Should().BeNull("不得自定义 DNS、IP 或 Socket 连接流程");
        socketsHandler.ConnectTimeout.Should().Be(TimeSpan.FromSeconds(10));
        socketsHandler.PooledConnectionLifetime.Should().Be(TimeSpan.FromMinutes(5));
        socketsHandler.PooledConnectionIdleTimeout.Should().Be(TimeSpan.FromMinutes(1));
        socketsHandler.SslOptions.RemoteCertificateValidationCallback.Should().BeNull();
    }

    [Fact]
    public async Task 安全Handler_真实TLS应发送配置主机SNI并拒绝错误证书()
    {
        using var certificate = CreateWrongHostCertificate();
        using var listener = new TcpListener(IPAddress.IPv6Any, 0);
        listener.Server.DualMode = true;
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var observedSni = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = ServeTlsOnceAsync(listener, certificate, observedSni);
        using var factory = new SafeAiHttpMessageHandlerFactory();
        using var client = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            $"https://localhost:{port}",
            TimeSpan.FromSeconds(5));

        var action = () => client.GetAsync($"https://localhost:{port}/models");

        var exception = await Record.ExceptionAsync(action);
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        exception.Should().BeOfType<HttpRequestException>();
        (await observedSni.Task.WaitAsync(TimeSpan.FromSeconds(5)))
            .Should().Be("localhost");
    }

    [Fact]
    public void 安全客户端工厂_同提供商同Origin应复用连接池()
    {
        using var factory = new SafeAiHttpMessageHandlerFactory();
        using var first = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            "https://模型.example/v1");
        using var second = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            "https://xn--xgs754b.example.:443/other");

        GetPoolCache(factory).Count.Should().Be(1);
    }

    [Fact]
    public void 安全客户端工厂_非法Timeout应在取得连接池Lease前拒绝()
    {
        using var factory = new SafeAiHttpMessageHandlerFactory();

        var action = () => factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            "http://models.internal:8080",
            TimeSpan.Zero);

        action.Should().Throw<ArgumentOutOfRangeException>();
        GetPoolCache(factory).Count.Should().Be(0);
    }

    [Fact]
    public void 安全客户端工厂_连接池缓存应有界且淘汰不应中断活跃Lease()
    {
        using var factory = new SafeAiHttpMessageHandlerFactory();
        using var activeClient = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            "http://models-0.example:8080");
        var firstEntry = GetPoolCache(factory).Values.Cast<object>().Single();

        for (var index = 1; index <= 64; index++)
        {
            using var client = factory.CreateClient(
                AiServiceType.CustomOpenAICompatible,
                $"http://models-{index}.example:8080");
        }

        GetPoolCache(factory).Count.Should().Be(64);
        ReadBool(firstEntry, "_retired").Should().BeTrue();
        ReadBool(firstEntry, "_disposed").Should().BeFalse(
            "被淘汰连接池仍有活动 lease 时不得中断该调用方");
    }

    [Fact]
    public void 安全客户端工厂_关闭时应等待活动Lease并且仅释放一次()
    {
        var factory = new SafeAiHttpMessageHandlerFactory();
        var activeClient = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            "http://models.example:8080");
        var entry = GetPoolCache(factory).Values.Cast<object>().Single();

        factory.Dispose();
        factory.Dispose();

        ReadBool(entry, "_retired").Should().BeTrue();
        ReadBool(entry, "_disposed").Should().BeFalse();
        activeClient.Dispose();
        activeClient.Dispose();
        ReadBool(entry, "_disposed").Should().BeTrue();
    }

    [Fact]
    public async Task 安全Handler_收到302时应返回首跳响应且不访问第二跳()
    {
        var firstPort = GetFreeTcpPort();
        var secondPort = GetFreeTcpPort();
        using var first = StartListener(firstPort);
        using var second = StartListener(secondPort);
        var secondVisited = false;
        var firstTask = Task.Run(async () =>
        {
            var context = await first.GetContextAsync();
            context.Response.StatusCode = (int)HttpStatusCode.Found;
            context.Response.RedirectLocation = $"http://127.0.0.1:{secondPort}/second";
            context.Response.Close();
        });
        var secondTask = Task.Run(async () =>
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            try
            {
                _ = await second.GetContextAsync().WaitAsync(cancellation.Token);
                secondVisited = true;
            }
            catch (OperationCanceledException)
            {
            }
        });
        using var factory = new SafeAiHttpMessageHandlerFactory();
        using var client = factory.CreateClient(
            AiServiceType.CustomOpenAICompatible,
            $"http://127.0.0.1:{firstPort}");

        using var response = await client.GetAsync($"http://127.0.0.1:{firstPort}/first");
        await firstTask;
        await secondTask;

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        secondVisited.Should().BeFalse();
    }

    [Fact]
    public async Task 安全Handler_存在全局代理时请求仍应直连配置Origin()
    {
        var port = GetFreeTcpPort();
        using var listener = StartListener(port);
        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.Close();
        });
        var originalProxy = WebRequest.DefaultWebProxy;
        try
        {
            WebRequest.DefaultWebProxy = new WebProxy("http://127.0.0.1:9");
            using var factory = new SafeAiHttpMessageHandlerFactory();
            using var client = factory.CreateClient(
                AiServiceType.CustomOpenAICompatible,
                $"http://127.0.0.1:{port}");

            using var response = await client.GetAsync($"http://127.0.0.1:{port}/models");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            await serverTask;
        }
        finally
        {
            WebRequest.DefaultWebProxy = originalProxy;
        }
    }

    private static HttpListener StartListener(int port)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        return listener;
    }

    private static async Task<bool> ObserveSingleRequestAsync(HttpListener listener)
    {
        try
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.Close();
            return true;
        }
        catch (HttpListenerException) when (!listener.IsListening)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static X509Certificate2 CreateWrongHostCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=wrong.example",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection
                {
                    new("1.3.6.1.5.5.7.3.1")
                },
                false));
        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddDnsName("wrong.example");
        request.CertificateExtensions.Add(subjectAlternativeNames.Build());
        using var generated = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        const string password = "task11-test-certificate";
        return new X509Certificate2(
            generated.Export(X509ContentType.Pkcs12, password),
            password,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
    }

    private static async Task ServeTlsOnceAsync(
        TcpListener listener,
        X509Certificate2 certificate,
        TaskCompletionSource<string?> observedSni)
    {
        try
        {
            using var tcpClient = await listener.AcceptTcpClientAsync();
            await using var sslStream = new SslStream(tcpClient.GetStream());
            var options = new SslServerAuthenticationOptions
            {
                ServerCertificateSelectionCallback = (_, hostName) =>
                {
                    observedSni.TrySetResult(hostName);
                    return certificate;
                },
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            };

            await sslStream.AuthenticateAsServerAsync(options);
            using var reader = new StreamReader(
                sslStream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
            {
            }

            await sslStream.WriteAsync(
                Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"));
        }
        catch (AuthenticationException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static SocketsHttpHandler FindSocketsHandler(HttpMessageHandler handler)
    {
        var current = handler;
        while (current is DelegatingHandler delegating)
            current = delegating.InnerHandler;

        return current.Should().BeOfType<SocketsHttpHandler>().Subject;
    }

    private static System.Collections.IDictionary GetPoolCache(
        SafeAiHttpMessageHandlerFactory factory)
    {
        return typeof(SafeAiHttpMessageHandlerFactory)
            .GetField("_poolCache", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(factory)
            .Should().BeAssignableTo<System.Collections.IDictionary>().Subject;
    }

    private static bool ReadBool(object target, string fieldName)
    {
        return (bool)target.GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(target)!;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}

[CollectionDefinition("AI安全传输全局代理隔离", DisableParallelization = true)]
public sealed class Ai安全传输全局代理隔离Collection;
