using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    public void CreateChatCompletionService_OllamaDisableThinkingChanged_ShouldUseDifferentCachedInstances()
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
        first.GetType().Name.Should().Be("OllamaNativeChatCompletionService");
        second.GetType().Name.Should().Be("OllamaNativeChatCompletionService");
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
        var httpClientField = chatService.GetType().GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic);
        httpClientField.Should().NotBeNull();
        httpClientField!.GetValue(chatService).Should().BeSameAs(expectedClient);
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
    public void SemanticKernelServiceFactory_安全策略代次变化后不应复用旧服务实例()
    {
        var safeClientFactory = new FakeSafeAiHttpClientFactory(new HttpClient());
        using var factory = new SemanticKernelServiceFactory(
            NullLoggerFactory.Instance,
            safeClientFactory,
            Options.Create(new SemanticKernelOptions()));
        var config = new AiServiceConfigModel
        {
            Id = 42,
            ServiceType = AiServiceType.CustomOpenAICompatible,
            Endpoint = "https://models.example.com/v1",
            ApiKey = "test-placeholder",
            LlmModel = "chat-model"
        };

        var first = factory.CreateChatCompletionService(config);
        safeClientFactory.Generation = 2;
        var second = factory.CreateChatCompletionService(config);

        second.Should().NotBeSameAs(first);
        safeClientFactory.Calls.Should().HaveCount(2);
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

    private sealed class FakeSafeAiHttpClientFactory(HttpClient httpClient) : ISafeAiHttpClientFactory
    {
        public long Generation { get; set; } = 1;

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
}
