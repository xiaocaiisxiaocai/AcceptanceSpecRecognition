namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

/// <summary>
/// AI 服务不可用异常（包含可展示原因）
/// </summary>
public class AiServiceUnavailableException : Exception
{
    public AiServiceUnavailableException(string reason, IReadOnlyList<string>? details = null, Exception? innerException = null)
        : base(reason, innerException)
    {
        Reason = reason;
        Details = details ?? Array.Empty<string>();
    }

    public string Reason { get; }

    public IReadOnlyList<string> Details { get; }
}
