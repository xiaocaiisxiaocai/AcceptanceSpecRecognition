using AcceptanceSpecSystem.Api.Controllers;
using AcceptanceSpecSystem.Api.Middleware;
using AcceptanceSpecSystem.Api.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Tests;

public class BaseApiControllerTests
{
    [Theory]
    [InlineData(401, StatusCodes.Status401Unauthorized)]
    [InlineData(403, StatusCodes.Status403Forbidden)]
    [InlineData(404, StatusCodes.Status404NotFound)]
    [InlineData(409, StatusCodes.Status409Conflict)]
    [InlineData(413, StatusCodes.Status413PayloadTooLarge)]
    [InlineData(422, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(429, StatusCodes.Status429TooManyRequests)]
    [InlineData(500, StatusCodes.Status500InternalServerError)]
    [InlineData(599, StatusCodes.Status500InternalServerError)]
    [InlineData(499, StatusCodes.Status400BadRequest)]
    [InlineData(600, StatusCodes.Status400BadRequest)]
    public void Resolve_应映射稳定业务错误码(int code, int expected)
    {
        ApiHttpStatusMapper.Resolve(code).Should().Be(expected);
    }

    [Fact]
    public void Error_应按业务错误码返回真实状态并携带跟踪标识()
    {
        const string traceId = "trace-controller-conflict";
        var controller = CreateController(traceId);

        var actionResult = controller.CreateError(409, "数据已被其他请求修改");

        var objectResult = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var response = objectResult.Value.Should().BeOfType<ApiResponse>().Subject;
        response.Code.Should().Be(409);
        response.Message.Should().Be("数据已被其他请求修改");
        response.TraceId.Should().Be(traceId);
    }

    [Fact]
    public void 泛型Error_应按业务错误码返回真实状态并携带跟踪标识()
    {
        const string traceId = "trace-controller-not-found";
        var controller = CreateController(traceId);

        var actionResult = controller.CreateError<object>(404, "验收规格不存在");

        var objectResult = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        var response = objectResult.Value.Should().BeOfType<ApiResponse<object>>().Subject;
        response.Code.Should().Be(404);
        response.Message.Should().Be("验收规格不存在");
        response.TraceId.Should().Be(traceId);
    }

    [Fact]
    public void ErrorResult_文件下载分支应使用统一映射并携带跟踪标识()
    {
        const string traceId = "trace-download-unauthorized";
        var controller = CreateController(traceId);

        var result = controller.CreateErrorResult(401, "会话缺少用户上下文");

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        var response = objectResult.Value.Should().BeOfType<ApiResponse>().Subject;
        response.Code.Should().Be(401);
        response.Message.Should().Be("会话缺少用户上下文");
        response.TraceId.Should().Be(traceId);
    }

    private static TestApiController CreateController(string traceId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[RequestTracingMiddleware.TraceIdItemKey] = traceId;

        return new TestApiController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private sealed class TestApiController : BaseApiController
    {
        public ActionResult<ApiResponse> CreateError(int code, string message)
            => Error(code, message);

        public ActionResult<ApiResponse<T>> CreateError<T>(int code, string message)
            => Error<T>(code, message);

        public IActionResult CreateErrorResult(int code, string message)
            => ErrorResult(code, message);
    }
}
