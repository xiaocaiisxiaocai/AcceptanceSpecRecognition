using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AcceptanceSpecSystem.Core.Tests;

public class OllamaNativeChatCompletionServiceTests
{
    [Fact]
    public async Task SemanticKernelServiceFactory_GetOrCreateCached_ShouldCreateSingleInstance_PerKeyUnderConcurrency()
    {
        using var factory = new SemanticKernelServiceFactory(
            NullLoggerFactory.Instance,
            new FakeSafeAiHttpClientFactory(new HttpClient()),
            Options.Create(new SemanticKernelOptions()));

        var method = typeof(SemanticKernelServiceFactory)
            .GetMethod("GetOrCreateCached", BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(TestDisposableService));

        const int concurrentCount = 12;
        using var startGate = new ManualResetEventSlim(false);
        var createdCount = 0;
        var tasks = Enumerable.Range(0, concurrentCount)
            .Select(_ => Task.Run(() =>
            {
                startGate.Wait();
                return (TestDisposableService)method.Invoke(factory,
                [
                    "chat_concurrency_key",
                    new Func<TestDisposableService>(() =>
                    {
                        Interlocked.Increment(ref createdCount);
                        Thread.Sleep(80);
                        return new TestDisposableService();
                    })
                ])!;
            }))
            .ToArray();

        startGate.Set();
        var instances = await Task.WhenAll(tasks);

        createdCount.Should().Be(1);
        instances.Distinct().Should().ContainSingle();
    }

    [Fact]
    public void SemanticKernelServiceFactory_Ollama思考配置变化应创建不同服务实例()
    {
        var factory = new SemanticKernelServiceFactory(
            NullLoggerFactory.Instance,
            new FakeSafeAiHttpClientFactory(new HttpClient()),
            Options.Create(new SemanticKernelOptions()));
        var config = new AiServiceConfigModel
        {
            Id = 7,
            ServiceType = AiServiceType.Ollama,
            Endpoint = "http://127.0.0.1:11434/api",
            LlmModel = "qwen3.5:35b",
            DisableThinking = false
        };

        var first = factory.CreateChatCompletionService(config);

        config.DisableThinking = true;
        var second = factory.CreateChatCompletionService(config);

        first.Should().NotBeSameAs(second);
        first.Attributes["service"].Should().Be("ollama-native-chat");
        second.Attributes["service"].Should().Be("ollama-native-chat");
    }

    [Fact]
    public void SemanticKernelServiceFactory_Ollama聊天应使用安全客户端工厂()
    {
        var expectedClient = new HttpClient();
        var safeClientFactory = new FakeSafeAiHttpClientFactory(expectedClient);
        using var factory = new SemanticKernelServiceFactory(
            NullLoggerFactory.Instance,
            safeClientFactory,
            Options.Create(new SemanticKernelOptions()));
        var config = new AiServiceConfigModel
        {
            Id = 15,
            ServiceType = AiServiceType.Ollama,
            Endpoint = "http://127.0.0.1:11434/api",
            LlmModel = "qwen3.5:35b"
        };

        var chatService = factory.CreateChatCompletionService(config);

        safeClientFactory.Calls.Should().ContainSingle();
        safeClientFactory.Calls[0].ServiceType.Should().Be(AiServiceType.Ollama);
        safeClientFactory.Calls[0].Endpoint.Should().Be("http://127.0.0.1:11434");
        chatService.Attributes["service"].Should().Be("ollama-native-chat");
    }

    [Theory]
    [InlineData(AiServiceType.OpenAI, "https://api.openai.com/v1", true)]
    [InlineData(AiServiceType.AzureOpenAI, "https://azure.example.com", true)]
    [InlineData(AiServiceType.LMStudio, "http://127.0.0.1:1234/v1", true)]
    [InlineData(AiServiceType.CustomOpenAICompatible, "https://models.example.com/v1", true)]
    [InlineData(AiServiceType.OpenAI, "https://api.openai.com/v1", false)]
    [InlineData(AiServiceType.AzureOpenAI, "https://azure.example.com", false)]
    [InlineData(AiServiceType.LMStudio, "http://127.0.0.1:1234/v1", false)]
    [InlineData(AiServiceType.CustomOpenAICompatible, "https://models.example.com/v1", false)]
    public void SemanticKernelServiceFactory_各提供商聊天和Embedding应统一使用安全客户端(
        AiServiceType serviceType,
        string endpoint,
        bool chat)
    {
        var safeClientFactory = new FakeSafeAiHttpClientFactory(new HttpClient());
        using var factory = new SemanticKernelServiceFactory(
            NullLoggerFactory.Instance,
            safeClientFactory,
            Options.Create(new SemanticKernelOptions()));
        var config = new AiServiceConfigModel
        {
            Id = 41,
            ServiceType = serviceType,
            Endpoint = endpoint,
            ApiKey = "test-placeholder",
            LlmModel = "chat-model",
            EmbeddingModel = "embedding-model"
        };

        if (chat)
            _ = factory.CreateChatCompletionService(config);
        else
            _ = factory.CreateEmbeddingGenerator(config);

        safeClientFactory.Calls.Should().ContainSingle();
        safeClientFactory.Calls[0].ServiceType.Should().Be(serviceType);
        safeClientFactory.Calls[0].Endpoint.Should().Be(endpoint);
    }

    [Fact]
    public void SemanticKernelServiceFactory_超过64项时应确定性淘汰OpenAI和Azure聊天及Embedding客户端()
    {
        var safeClientFactory = new TrackingSafeAiHttpClientFactory();
        var factory = new SemanticKernelServiceFactory(
            NullLoggerFactory.Instance,
            safeClientFactory,
            Options.Create(new SemanticKernelOptions()));

        for (var index = 0; index < 68; index++)
        {
            var azure = index is 2 or 3;
            var embedding = index is 1 or 3;
            var config = new AiServiceConfigModel
            {
                Id = index + 1,
                ServiceType = azure ? AiServiceType.AzureOpenAI : AiServiceType.OpenAI,
                Endpoint = azure
                    ? $"https://azure-{index}.example.com"
                    : $"https://models-{index}.example.com/v1",
                ApiKey = "test-placeholder",
                LlmModel = "chat-model",
                EmbeddingModel = "embedding-model"
            };
            if (embedding)
                _ = factory.CreateEmbeddingGenerator(config);
            else
                _ = factory.CreateChatCompletionService(config);
        }

        safeClientFactory.Clients.Should().HaveCount(68);
        safeClientFactory.Clients.Take(4).Should().OnlyContain(client => client.DisposeCount == 1);
        safeClientFactory.Clients.Skip(4).Should().OnlyContain(client => client.DisposeCount == 0);

        factory.Dispose();
        factory.Dispose();
        safeClientFactory.Clients.Should().OnlyContain(client => client.DisposeCount == 1);
    }

    [Fact]
    public void SemanticKernelServiceFactory_清出64个过期Owner后新建失败仍应立即且仅一次释放()
    {
        var safeClientFactory = new TrackingSafeAiHttpClientFactory();
        var factory = new SemanticKernelServiceFactory(
            NullLoggerFactory.Instance,
            safeClientFactory,
            Options.Create(new SemanticKernelOptions()));

        for (var index = 0; index < 64; index++)
        {
            _ = factory.CreateChatCompletionService(new AiServiceConfigModel
            {
                Id = index + 1,
                ServiceType = AiServiceType.OpenAI,
                Endpoint = $"https://models-{index}.example.com/v1",
                ApiKey = "test-placeholder",
                LlmModel = "chat-model"
            });
        }
        AgeAllCachedServices(factory, TimeSpan.FromHours(1));

        var action = () => factory.CreateChatCompletionService(new AiServiceConfigModel
        {
            Id = 999,
            ServiceType = AiServiceType.OpenAI,
            Endpoint = "不是合法端点",
            ApiKey = "test-placeholder",
            LlmModel = "chat-model"
        });

        action.Should().Throw<InvalidOperationException>();
        safeClientFactory.Clients.Should().HaveCount(64);
        safeClientFactory.Clients.Should().OnlyContain(client => client.DisposeCount == 1);
        safeClientFactory.Clients
            .Select(client => client.Handler)
            .Should().AllBeOfType<TrackingHttpMessageHandler>()
            .Which.Should().OnlyContain(handler => handler.DisposeCount == 1);

        factory.Dispose();
        factory.Dispose();
        safeClientFactory.Clients.Should().OnlyContain(client => client.DisposeCount == 1);
    }

    [Fact]
    public void SemanticKernelServiceFactory_关闭时应释放所有提供商聊天和Embedding客户端且仅一次()
    {
        var safeClientFactory = new TrackingSafeAiHttpClientFactory();
        var factory = new SemanticKernelServiceFactory(
            NullLoggerFactory.Instance,
            safeClientFactory,
            Options.Create(new SemanticKernelOptions()));
        var providers = new[]
        {
            (AiServiceType.OpenAI, "https://api.openai.com/v1"),
            (AiServiceType.AzureOpenAI, "https://azure.example.com"),
            (AiServiceType.CustomOpenAICompatible, "https://models.example.com/v1"),
            (AiServiceType.LMStudio, "http://127.0.0.1:1234/v1"),
            (AiServiceType.Ollama, "http://127.0.0.1:11434")
        };

        foreach (var (serviceType, endpoint) in providers)
        {
            var config = new AiServiceConfigModel
            {
                Id = 100 + (int)serviceType,
                ServiceType = serviceType,
                Endpoint = endpoint,
                ApiKey = "test-placeholder",
                LlmModel = "chat-model",
                EmbeddingModel = "embedding-model"
            };
            _ = factory.CreateChatCompletionService(config);
            _ = factory.CreateEmbeddingGenerator(config);
        }

        factory.Dispose();
        factory.Dispose();

        safeClientFactory.Clients.Should().HaveCount(providers.Length * 2);
        safeClientFactory.Clients.Should().OnlyContain(client => client.DisposeCount == 1);
    }

    [Fact]
    public async Task SemanticKernelServiceFactory_关闭时不应中断活跃OpenAI请求且完成后应释放客户端()
    {
        using var requestGate = new GatedOpenAiHandler();
        var safeClientFactory = new TrackingSafeAiHttpClientFactory(() => requestGate);
        var factory = new SemanticKernelServiceFactory(
            NullLoggerFactory.Instance,
            safeClientFactory,
            Options.Create(new SemanticKernelOptions()));
        var service = factory.CreateChatCompletionService(new AiServiceConfigModel
        {
            Id = 91,
            ServiceType = AiServiceType.OpenAI,
            Endpoint = "https://api.openai.com/v1",
            ApiKey = "test-placeholder",
            LlmModel = "chat-model"
        });
        var history = new ChatHistory();
        history.AddUserMessage("你好");

        var requestTask = service.GetChatMessageContentsAsync(history);
        await requestGate.WaitUntilStartedAsync();
        factory.Dispose();

        safeClientFactory.Clients.Should().ContainSingle();
        safeClientFactory.Clients[0].DisposeCount.Should().Be(0);

        requestGate.AllowResponse();
        var result = await requestTask;

        result.Should().ContainSingle();
        result[0].Content.Should().Be("ok");
        safeClientFactory.Clients[0].DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task SemanticKernelServiceFactory_流式枚举结束前Retire不得释放客户端()
    {
        var inner = new GatedStreamingChatService();
        var client = new TrackingHttpClient(new HttpClientHandler());
        using var service = new SemanticKernelServiceFactory.OwnedChatCompletionService(
            inner,
            client);
        await using var enumerator = service
            .GetStreamingChatMessageContentsAsync(new ChatHistory())
            .GetAsyncEnumerator();

        (await enumerator.MoveNextAsync()).Should().BeTrue();
        service.Dispose();
        client.DisposeCount.Should().Be(0);

        inner.AllowCompletion();
        (await enumerator.MoveNextAsync()).Should().BeFalse();
        client.DisposeCount.Should().Be(1);

        service.Dispose();
        client.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task SemanticKernelServiceFactory_Embedding生成结束前Retire不得释放客户端()
    {
        var inner = new GatedEmbeddingGenerator();
        var client = new TrackingHttpClient(new HttpClientHandler());
        using var generator = new SemanticKernelServiceFactory.OwnedEmbeddingGenerator(
            inner,
            client);

        var generationTask = generator.GenerateAsync(["input"]);
        await inner.WaitUntilStartedAsync();
        generator.Dispose();
        client.DisposeCount.Should().Be(0);

        inner.AllowCompletion();
        var result = await generationTask;

        result.Should().ContainSingle();
        client.DisposeCount.Should().Be(1);
        generator.Dispose();
        client.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task OllamaNativeChatCompletionService_ShouldPostApiChat_WithThinkFalse()
    {
        var port = GetFreeTcpPort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        string? requestLine = null;
        string? requestBody = null;

        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            requestLine = $"{context.Request.HttpMethod} {context.Request.RawUrl} HTTP/{context.Request.ProtocolVersion}";
            using var bodyReader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            requestBody = await bodyReader.ReadToEndAsync();

            var responseJson = """
                {
                  "message": { "role": "assistant", "content": "ok" },
                  "done": true,
                  "done_reason": "stop",
                  "total_duration": 1000,
                  "load_duration": 10,
                  "prompt_eval_duration": 20,
                  "eval_duration": 30,
                  "prompt_eval_count": 1,
                  "eval_count": 1
                }
                """;
            var responseBytes = Encoding.UTF8.GetBytes(responseJson);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes);
            context.Response.Close();
        });

        var service = CreateOllamaNativeService(
            new AiServiceConfigModel
            {
                Id = 11,
                ServiceType = AiServiceType.Ollama,
                Endpoint = $"http://127.0.0.1:{port}/api",
                LlmModel = "qwen3.5:35b",
                DisableThinking = true
            });

        var history = new ChatHistory();
        history.AddUserMessage("你好");

        var results = await service.GetChatMessageContentsAsync(history);
        await serverTask;

        results.Should().ContainSingle();
        results[0].Content.Should().Be("ok");
        requestLine.Should().StartWith("POST /api/chat HTTP/");

        using var json = JsonDocument.Parse(requestBody!);
        json.RootElement.GetProperty("model").GetString().Should().Be("qwen3.5:35b");
        json.RootElement.GetProperty("stream").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("think").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("keep_alive").GetString().Should().Be("30m");
        json.RootElement.GetProperty("messages").GetArrayLength().Should().Be(1);
        json.RootElement.GetProperty("messages")[0].GetProperty("role").GetString().Should().Be("user");
        json.RootElement.GetProperty("messages")[0].GetProperty("content").GetString().Should().Be("你好");

        // 未显式传 executionSettings 时，应默认带上确定性采样选项（temperature=0 + 固定 seed），
        // 保证同输入裁决结果可复现；缺失任一项都会回到 Ollama 默认随机采样。
        json.RootElement.TryGetProperty("options", out var options).Should().BeTrue("请求体必须携带 options 采样选项");
        options.GetProperty("temperature").GetDouble().Should().Be(0);
        options.GetProperty("seed").GetInt32().Should().Be(42);
    }

    private static IChatCompletionService CreateOllamaNativeService(AiServiceConfigModel config)
    {
        var assembly = typeof(SemanticKernelServiceFactory).Assembly;
        var serviceType = assembly.GetType("AcceptanceSpecSystem.Core.AI.SemanticKernel.OllamaNativeChatCompletionService", throwOnError: true)!;
        var loggerType = typeof(NullLogger<>).MakeGenericType(serviceType);
        var logger = loggerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? Activator.CreateInstance(loggerType, nonPublic: true);
        var instance = Activator.CreateInstance(serviceType, config, new HttpClient(), logger);
        return (IChatCompletionService)instance!;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void AgeAllCachedServices(
        SemanticKernelServiceFactory factory,
        TimeSpan age)
    {
        var cacheField = typeof(SemanticKernelServiceFactory)
            .GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic);
        cacheField.Should().NotBeNull();
        var cache = cacheField!.GetValue(factory)
            .Should().BeAssignableTo<System.Collections.IDictionary>().Subject;
        foreach (var entry in cache.Values.Cast<object>())
        {
            var lastAccessField = entry.GetType()
                .GetField("<LastAccess>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            lastAccessField.Should().NotBeNull();
            lastAccessField!.SetValue(entry, DateTimeOffset.UtcNow - age);
        }
    }

    private sealed class FakeSafeAiHttpClientFactory(HttpClient httpClient) : ISafeAiHttpClientFactory
    {
        public List<(AiServiceType ServiceType, string Endpoint)> Calls { get; } = [];

        public HttpClient CreateClient(
            AiServiceType serviceType,
            string endpoint,
            TimeSpan? timeout = null)
        {
            Calls.Add((serviceType, endpoint));
            return httpClient;
        }
    }

    private sealed class TestDisposableService : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class TrackingSafeAiHttpClientFactory(
        Func<HttpMessageHandler>? handlerFactory = null) : ISafeAiHttpClientFactory
    {
        public List<TrackingHttpClient> Clients { get; } = [];

        public HttpClient CreateClient(
            AiServiceType serviceType,
            string endpoint,
            TimeSpan? timeout = null)
        {
            var client = new TrackingHttpClient(
                handlerFactory?.Invoke() ?? new TrackingHttpMessageHandler());
            Clients.Add(client);
            return client;
        }
    }

    private sealed class TrackingHttpClient(HttpMessageHandler handler)
        : HttpClient(handler)
    {
        public HttpMessageHandler Handler { get; } = handler;

        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCount++;
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingHttpMessageHandler : HttpMessageHandler
    {
        public int DisposeCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCount++;
            base.Dispose(disposing);
        }
    }

    private sealed class GatedOpenAiHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _allowResponse =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitUntilStartedAsync() =>
            _started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void AllowResponse() => _allowResponse.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await _allowResponse.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "id": "chatcmpl-test",
                      "object": "chat.completion",
                      "created": 1,
                      "model": "chat-model",
                      "choices": [
                        {
                          "index": 0,
                          "message": { "role": "assistant", "content": "ok" },
                          "finish_reason": "stop"
                        }
                      ],
                      "usage": {
                        "prompt_tokens": 1,
                        "completion_tokens": 1,
                        "total_tokens": 2
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class GatedStreamingChatService : IChatCompletionService
    {
        private readonly TaskCompletionSource _allowCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyDictionary<string, object?> Attributes { get; } =
            new Dictionary<string, object?>();

        public void AllowCompletion() => _allowCompletion.TrySetResult();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Microsoft.SemanticKernel.Kernel? kernel = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Microsoft.SemanticKernel.Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            yield return new StreamingChatMessageContent(
                AuthorRole.Assistant,
                "part");
            await _allowCompletion.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class GatedEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _allowCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitUntilStartedAsync() =>
            _started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void AllowCompletion() => _allowCompletion.TrySetResult();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _allowCompletion.Task.WaitAsync(cancellationToken);
            return new GeneratedEmbeddings<Embedding<float>>(
                [new Embedding<float>(new float[] { 1f })]);
        }

        public void Dispose()
        {
        }
    }
}
