using System.Net;
using System.Net.Sockets;
using System.Reflection;
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
