namespace AcceptanceSpecSystem.Core.Matching.Models;

/// <summary>
/// AI 等价裁决结论
/// </summary>
public enum LlmEquivalenceVerdict
{
    Equivalent = 1,
    Different = 2,
    Uncertain = 3
}

/// <summary>
/// AI 等价裁决原因类型
/// </summary>
public enum LlmEquivalenceReasonType
{
    FormatOnly = 1,
    PunctuationOnly = 2,
    EquivalentExpression = 3,
    SymbolEquivalent = 4,
    SemanticDifference = 5,
    SymbolConflict = 6,
    Uncertain = 7
}

/// <summary>
/// AI 等价裁决请求
/// </summary>
public sealed class LlmEquivalenceAdjudicationRequest
{
    public string SourceProject { get; set; } = string.Empty;

    public string SourceSpecification { get; set; } = string.Empty;

    public string CandidateProject { get; set; } = string.Empty;

    public string CandidateSpecification { get; set; } = string.Empty;

    public string CurrentDecision { get; set; } = "manualReview";

    public Dictionary<string, double> ScoreDetails { get; set; } = [];

    public List<string> EvidenceSummary { get; set; } = [];

    public List<string> ConflictSummary { get; set; } = [];

    public int? LlmServiceId { get; set; }
}

/// <summary>
/// AI 等价裁决结果
/// </summary>
public sealed class LlmEquivalenceAdjudicationResult
{
    public LlmEquivalenceVerdict Verdict { get; set; } = LlmEquivalenceVerdict.Uncertain;

    public LlmEquivalenceReasonType ReasonType { get; set; } = LlmEquivalenceReasonType.Uncertain;

    public double Confidence { get; set; }

    public string? Reason { get; set; }
}
