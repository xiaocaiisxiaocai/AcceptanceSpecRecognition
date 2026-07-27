using System.Reflection;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Middleware;
using AcceptanceSpecSystem.Application;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Api.Tests;

public class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenUnhandledExceptionContainsBusinessContent_ShouldNotAttachExceptionOrMessageToLog()
    {
        const string sensitiveText = "客户验收规格-LOG-UNIQUE";
        var logger = new CollectingLogger<ExceptionHandlingMiddleware>();
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException(sensitiveText),
            logger);
        var context = new DefaultHttpContext();
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await middleware.InvokeAsync(context);

        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error);
        logger.Entries.Select(entry => entry.Exception).Should().OnlyContain(exception => exception == null);
        logger.Entries.Should().OnlyContain(entry => !entry.Message.Contains(sensitiveText, StringComparison.Ordinal));
    }

    [Fact]
    public void ExceptionHandlingMiddleware_ShouldCacheJsonSerializerOptions()
    {
        var field = typeof(ExceptionHandlingMiddleware)
            .GetField("JsonOptions", BindingFlags.Static | BindingFlags.NonPublic);

        field.Should().NotBeNull();
        field!.GetValue(null).Should().BeOfType<JsonSerializerOptions>()
            .Which.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
    }

    [Fact]
    public async Task InvokeAsync_WhenOperationCanceledWithoutRequestAbort_ShouldReturn408()
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new OperationCanceledException("timeout"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status408RequestTimeout);
        context.Response.ContentType.Should().Be("application/json; charset=utf-8");

        responseBody.Position = 0;
        var document = await JsonDocument.ParseAsync(responseBody);
        document.RootElement.GetProperty("code").GetInt32().Should().Be(408);
        document.RootElement.GetProperty("message").GetString().Should().Be("请求已取消");
    }

    [Fact]
    public async Task InvokeAsync_WhenRequestAlreadyAborted_ShouldNotWriteErrorResponse()
    {
        using var cts = new CancellationTokenSource();
        await using var responseBody = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestAborted = cts.Token
        };
        context.Response.Body = responseBody;
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new OperationCanceledException("client aborted"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        cts.Cancel();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        responseBody.Length.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_WhenArgumentExceptionContainsSensitiveDetail_ShouldReturnSanitizedMessage()
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new ArgumentException("server=db;password=secret; endpoint=https://internal.example"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        responseBody.Position = 0;
        var document = await JsonDocument.ParseAsync(responseBody);
        document.RootElement.GetProperty("message").GetString().Should().Be("请求参数错误");
    }

    [Fact]
    public async Task InvokeAsync_未知异常应返回稳定500且不泄漏内部信息()
    {
        const string traceId = "trace-unknown-exception";
        const string sensitiveMessage =
            "SqlException: SELECT * FROM AcceptanceSpecs; path=D:\\internal\\secrets\\appsettings.json";
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new Exception(sensitiveMessage),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Items[RequestTracingMiddleware.TraceIdItemKey] = traceId;
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody, leaveOpen: true);
        var json = await reader.ReadToEndAsync();
        json.Should().NotContain(nameof(Exception));
        json.Should().NotContain("SqlException");
        json.Should().NotContain("SELECT *");
        json.Should().NotContain(@"D:\internal");
        json.Should().NotContain(sensitiveMessage);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("code").GetInt32().Should().Be(500);
        document.RootElement.GetProperty("message").GetString()
            .Should().Be("服务器内部错误，请稍后重试");
        document.RootElement.GetProperty("traceId").GetString()
            .Should().Be(traceId).And.NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task InvokeAsync_业务异常应按稳定错误码返回真实状态()
    {
        const string traceId = "trace-business-conflict";
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new ApplicationServiceException(409, "数据已被其他请求修改"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Items[RequestTracingMiddleware.TraceIdItemKey] = traceId;
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        responseBody.Position = 0;
        var document = await JsonDocument.ParseAsync(responseBody);
        document.RootElement.GetProperty("code").GetInt32().Should().Be(409);
        document.RootElement.GetProperty("message").GetString()
            .Should().Be("数据已被其他请求修改");
        document.RootElement.GetProperty("traceId").GetString().Should().Be(traceId);
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, exception, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception, string Message);
}
