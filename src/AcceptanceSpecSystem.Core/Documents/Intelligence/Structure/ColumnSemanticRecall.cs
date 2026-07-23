namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;

public sealed class LlmColumnSemanticRecallRequest
{
    public int TableIndex { get; init; }

    public string? TableName { get; init; }

    public IReadOnlyList<string> Headers { get; init; } = [];

    public IReadOnlyList<ColumnSemanticRecallHeaderCandidate> UnmappedHeaders { get; init; } = [];

    public IReadOnlyDictionary<string, int?> MappedFields { get; init; } = new Dictionary<string, int?>();

    public IReadOnlyList<IReadOnlyList<string>> SampleRows { get; init; } = [];

    public int? LlmServiceId { get; init; }
}

public sealed class ColumnSemanticRecallHeaderCandidate
{
    public int ColumnIndex { get; init; }

    public string Header { get; init; } = string.Empty;
}

public sealed class LlmColumnSemanticRecallResult
{
    public IReadOnlyList<LlmColumnSemanticRecallSuggestion> Suggestions { get; init; } = [];
}

public sealed class LlmColumnSemanticRecallSuggestion
{
    public int ColumnIndex { get; init; }

    public string Header { get; init; } = string.Empty;

    public string TargetField { get; init; } = "Unknown";

    public double Confidence { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string Source { get; init; } = "SemanticRecall";
}

public interface ILlmColumnSemanticRecallService
{
    Task<LlmColumnSemanticRecallResult?> RecallAsync(
        LlmColumnSemanticRecallRequest request,
        CancellationToken cancellationToken = default);
}
