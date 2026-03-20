using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace AcceptanceSpecSystem.Core.Tests;

public class OllamaNativeChatCompletionServiceTests
{
    [Fact]
    public void CreateChatCompletionService_OllamaDisableThinkingChanged_ShouldUseDifferentCachedInstances()
    {
        var factory = new SemanticKernelServiceFactory(NullLoggerFactory.Instance);
        var config = new AiServiceConfig
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
            new AiServiceConfig
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
    }

    private static IChatCompletionService CreateOllamaNativeService(AiServiceConfig config)
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
}
