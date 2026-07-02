namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;

public enum DocumentStructureCandidateSource
{
    Rule = 0,
    Template = 1,
    Llm = 2,
    Fused = 3
}

public sealed class DocumentStructureCandidate
{
    public int TableIndex { get; init; }

    public string? TableName { get; init; }

    public int HeaderRowIndex { get; init; }

    public int HeaderRowCount { get; init; } = 1;

    public int DataStartRowIndex { get; init; } = 1;

    public int? DataEndRowIndex { get; init; }

    public int? ProjectColumnIndex { get; init; }

    public int? SpecificationColumnIndex { get; init; }

    public int? AcceptanceColumnIndex { get; init; }

    public int? RemarkColumnIndex { get; init; }

    public bool IsSpecificationOnly { get; init; }

    public double Confidence { get; init; }

    public DocumentStructureCandidateSource Source { get; init; }
}

public sealed class LlmDocumentStructureAdjudicationRequest
{
    public IReadOnlyList<DocumentStructureCandidate> RuleCandidates { get; init; } = [];

    public string DocumentTablesJson { get; init; } = string.Empty;

    public int? LlmServiceId { get; init; }
}

public sealed class LlmDocumentStructureAdjudicationResult
{
    public IReadOnlyList<DocumentStructureCandidate> Tables { get; init; } = [];

    public double Confidence { get; init; }

    public string Decision { get; init; } = "needConfirm";

    public string Reason { get; init; } = string.Empty;
}

public interface ILlmDocumentStructureAdjudicationService
{
    Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default);
}

public static class DocumentStructureFusion
{
    public static DocumentStructureCandidate Merge(
        DocumentStructureCandidate ruleCandidate,
        DocumentStructureCandidate? llmCandidate)
    {
        if (llmCandidate == null || llmCandidate.TableIndex != ruleCandidate.TableIndex)
        {
            return ruleCandidate;
        }

        return new DocumentStructureCandidate
        {
            TableIndex = ruleCandidate.TableIndex,
            TableName = ruleCandidate.TableName ?? llmCandidate.TableName,
            HeaderRowIndex = ruleCandidate.HeaderRowIndex,
            HeaderRowCount = ruleCandidate.HeaderRowCount,
            DataStartRowIndex = ruleCandidate.DataStartRowIndex,
            DataEndRowIndex = ruleCandidate.DataEndRowIndex ?? llmCandidate.DataEndRowIndex,
            ProjectColumnIndex = ruleCandidate.ProjectColumnIndex ?? llmCandidate.ProjectColumnIndex,
            SpecificationColumnIndex = ruleCandidate.SpecificationColumnIndex ?? llmCandidate.SpecificationColumnIndex,
            AcceptanceColumnIndex = ruleCandidate.AcceptanceColumnIndex ?? llmCandidate.AcceptanceColumnIndex,
            RemarkColumnIndex = ruleCandidate.RemarkColumnIndex ?? llmCandidate.RemarkColumnIndex,
            IsSpecificationOnly = ruleCandidate.IsSpecificationOnly && !llmCandidate.ProjectColumnIndex.HasValue,
            Confidence = Math.Max(ruleCandidate.Confidence, llmCandidate.Confidence),
            Source = DocumentStructureCandidateSource.Fused
        };
    }
}
