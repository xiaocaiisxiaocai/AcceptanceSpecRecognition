using Microsoft.AspNetCore.Http;

namespace AcceptanceSpecSystem.Api.Models;

/// <summary>
/// 将稳定业务错误码映射为真实 HTTP 状态。
/// </summary>
public static class ApiHttpStatusMapper
{
    /// <summary>
    /// 解析业务错误码对应的 HTTP 状态。
    /// </summary>
    public static int Resolve(int code) => code switch
    {
        401 => StatusCodes.Status401Unauthorized,
        403 => StatusCodes.Status403Forbidden,
        404 => StatusCodes.Status404NotFound,
        409 => StatusCodes.Status409Conflict,
        413 => StatusCodes.Status413PayloadTooLarge,
        422 => StatusCodes.Status422UnprocessableEntity,
        429 => StatusCodes.Status429TooManyRequests,
        >= 500 and <= 599 => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status400BadRequest
    };
}
