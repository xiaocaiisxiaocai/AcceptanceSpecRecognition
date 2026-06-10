namespace AcceptanceSpecSystem.Application;

/// <summary>
/// Application 层显式业务异常。
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
