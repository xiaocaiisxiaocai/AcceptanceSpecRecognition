using AcceptanceSpecSystem.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// API控制器基类
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public abstract class BaseApiController : ControllerBase
{
    /// <summary>
    /// 返回成功响应
    /// </summary>
    protected ActionResult<ApiResponse<T>> Success<T>(T? data, string message = "操作成功")
    {
        return Ok(ApiResponse<T>.Success(data, message));
    }

    /// <summary>
    /// 返回成功响应（无数据）
    /// </summary>
    protected ActionResult<ApiResponse> Success(string message = "操作成功")
    {
        return Ok(ApiResponse.Success(message));
    }

    /// <summary>
    /// 返回错误响应
    /// </summary>
    protected ActionResult<ApiResponse<T>> Error<T>(int code, string message)
    {
        return BadRequest(ApiResponse<T>.Error(code, message));
    }

    /// <summary>
    /// 返回错误响应（无数据）
    /// </summary>
    protected ActionResult<ApiResponse> Error(int code, string message)
    {
        return BadRequest(ApiResponse.Error(code, message));
    }

    /// <summary>
    /// 返回未找到响应
    /// </summary>
    protected ActionResult<ApiResponse<T>> NotFoundResult<T>(string message = "请求的资源不存在")
    {
        return NotFound(ApiResponse<T>.Error(404, message));
    }
}
