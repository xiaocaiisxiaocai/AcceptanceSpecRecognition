using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 匹配相关控制器基类，统一处理工作流服务返回值与异常。
/// </summary>
[Authorize]
public abstract class MatchingApiControllerBase : BaseApiController
{
    /// <summary>
    /// 统一处理工作流显式业务异常；其余异常继续交给全局异常中间件处理。
    /// </summary>
    protected async Task<ActionResult<ApiResponse<T>>> HandleAsync<T>(Func<Task<MatchingOperationResult<T>>> action)
    {
        try
        {
            var result = await action();
            return Success(result.Data, result.Message);
        }
        catch (MatchingApiException ex) when (ex.IsNotFound)
        {
            return NotFound(ApiResponse<T>.Error(404, ex.Message));
        }
        catch (MatchingApiException ex)
        {
            return Error<T>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 文件下载分支同样只消费显式业务异常；未知异常由全局中间件兜底。
    /// </summary>
    protected async Task<IActionResult> HandleFileAsync(Func<Task<MatchingDownloadResult>> action)
    {
        try
        {
            var result = await action();
            return File(result.Content, result.ContentType, result.FileName);
        }
        catch (MatchingApiException ex) when (ex.IsNotFound)
        {
            return NotFound(ApiResponse.Error(404, ex.Message));
        }
        catch (MatchingApiException ex)
        {
            return BadRequest(ApiResponse.Error(ex.Code, ex.Message));
        }
    }
}
