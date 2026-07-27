namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

public enum AiEndpointAccessFailureCategory
{
    RequestOriginMismatch
}

public sealed class AiEndpointAccessException : InvalidOperationException
{
    public AiEndpointAccessException(
        AiEndpointAccessFailureCategory category,
        string message) : base(message)
    {
        Category = category;
    }

    public AiEndpointAccessFailureCategory Category { get; }
}
