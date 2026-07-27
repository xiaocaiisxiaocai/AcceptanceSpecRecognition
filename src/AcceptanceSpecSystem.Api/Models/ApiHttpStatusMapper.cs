using Microsoft.AspNetCore.Http;

namespace AcceptanceSpecSystem.Api.Models;

/// <summary>
/// 将稳定业务错误码映射为真实 HTTP 状态。
/// </summary>
public static class ApiHttpStatusMapper
{
    public const string InternalServerErrorMessage = "服务器内部错误，请稍后重试";

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

    /// <summary>
    /// 服务端错误不向客户端暴露应用、数据库或基础设施异常详情。
    /// </summary>
    public static string ResolveMessage(int code, string message) =>
        code >= 500 ? InternalServerErrorMessage : message;
}
