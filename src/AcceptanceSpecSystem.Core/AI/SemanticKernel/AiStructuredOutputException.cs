namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

/// <summary>
/// AI 服务成功响应，但在有限格式修复后仍不符合场景结构化输出契约。
/// </summary>
public sealed class AiStructuredOutputException : Exception
{
    public AiStructuredOutputException(string message)
        : base(message)
    {
    }
}
