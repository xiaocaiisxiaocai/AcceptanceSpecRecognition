using System.Reflection;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Middleware;
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
