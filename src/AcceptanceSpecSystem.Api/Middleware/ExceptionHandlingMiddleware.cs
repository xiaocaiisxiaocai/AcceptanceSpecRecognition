using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Models;

namespace AcceptanceSpecSystem.Api.Middleware;

/// <summary>
/// 全局异常处理中间件
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    /// <summary>
    /// 创建异常处理中间件实例
    /// </summary>
    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// 处理请求
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        _logger.LogError(exception, "发生未处理的异常: {Message}", exception.Message);

        var response = context.Response;
        response.ContentType = "application/json; charset=utf-8";

        var (statusCode, code, message) = exception switch
        {
            ArgumentException argEx => (HttpStatusCode.BadRequest, 400, argEx.Message),
            KeyNotFoundException => (HttpStatusCode.NotFound, 404, "请求的资源不存在"),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, 401, "未授权访问"),
            InvalidOperationException opEx => (HttpStatusCode.BadRequest, 400, opEx.Message),
            _ => (HttpStatusCode.InternalServerError, 500, "服务器内部错误，请稍后重试")
        };

        response.StatusCode = (int)statusCode;

        var apiResponse = ApiResponse.Error(code, message);
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await response.WriteAsync(JsonSerializer.Serialize(apiResponse, options));
    }
}

/// <summary>
/// 中间件扩展方法
/// </summary>
public static class ExceptionHandlingMiddlewareExtensions
{
    /// <summary>
    /// 使用异常处理中间件
    /// </summary>
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
