namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 应用服务显式业务异常。
/// </summary>
public sealed class ApplicationServiceException : Exception
{
    public ApplicationServiceException(int code, string message)
        : base(message)
    {
        Code = code;
    }

    public int Code { get; }
}
