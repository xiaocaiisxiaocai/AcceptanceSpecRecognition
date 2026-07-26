namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 匹配/批量回复相关用例服务共享的 Result/Failure 帮助方法。
/// 此前 <see cref="MatchingOperationResult{T}"/> 与 <see cref="MatchingApiException"/> 的构造帮助方法
/// 在 BatchReplyAppService、MatchingCandidateProvider、MatchingPreviewAppService、MatchingTaskAppService、
/// MatchingTaskSnapshotService、MatchingWorkflowSupportService、SmartFillSpecBackfillAppService 等多个服务中
/// 重复定义，此处收敛为统一实现，各服务通过 <c>using static</c> 引入后直接复用。
/// </summary>
internal static class MatchingResultHelpers
{
    public static MatchingOperationResult<T> Result<T>(T data, string message = "操作成功")
    {
        return new MatchingOperationResult<T>(data, message);
    }

    /// <summary>
    /// 构造匹配相关业务异常；<paramref name="code"/> 为 404 时自动标记为“未找到”语义，
    /// 与既有各服务重复实现的行为保持一致。
    /// </summary>
    public static MatchingApiException Failure(int code, string message)
    {
        return new MatchingApiException(code, message, isNotFound: code == 404);
    }

    /// <summary>
    /// 构造仅携带错误信息、默认 400 状态码的匹配业务异常（MatchingCandidateProvider 等场景专用的单参数重载）。
    /// </summary>
    public static MatchingApiException Failure(string message)
    {
        return Failure(400, message);
    }

    public static MatchingApiException NotFoundFailure(string message)
    {
        return new MatchingApiException(404, message, isNotFound: true);
    }
}
