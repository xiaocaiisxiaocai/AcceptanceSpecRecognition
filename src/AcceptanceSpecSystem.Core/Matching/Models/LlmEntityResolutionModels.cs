namespace AcceptanceSpecSystem.Core.Matching.Models;

/// <summary>
/// LLM 实体关系判别结果
/// </summary>
public enum LlmEntityRelation
{
    Same = 1,
    AliasSame = 2,
    Conflict = 3,
    Unknown = 4
}

/// <summary>
/// LLM 实体判别请求
/// </summary>
public sealed class LlmEntityResolutionRequest
{
    public string SourceEntity { get; set; } = string.Empty;

    public string CandidateEntity { get; set; } = string.Empty;

    public string SourceText { get; set; } = string.Empty;

    public string CandidateText { get; set; } = string.Empty;

    public int? LlmServiceId { get; set; }
}

/// <summary>
/// LLM 实体判别响应
/// </summary>
public sealed class LlmEntityResolutionResult
{
    public LlmEntityRelation Relation { get; set; } = LlmEntityRelation.Unknown;

    public double Confidence { get; set; }

    public string? NormalizedEntity { get; set; }

    public string? Reason { get; set; }
}
