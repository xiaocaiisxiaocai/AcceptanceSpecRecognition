using System.Reflection;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Api.Tests;

public class ExceptionHandlingMiddlewareTests
{
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
}
