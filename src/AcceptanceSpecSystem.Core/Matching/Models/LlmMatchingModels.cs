namespace AcceptanceSpecSystem.Core.Matching.Models;

public enum LlmReviewScene
{
    MatchingReview = 1,
    ImportDuplicateReview = 2
}

/// <summary>
/// LLM 复核请求
/// </summary>
public class LlmReviewRequest
{
    public string SourceProject { get; set; } = string.Empty;
    public string SourceSpecification { get; set; } = string.Empty;
    public string BestMatchProject { get; set; } = string.Empty;
    public string BestMatchSpecification { get; set; } = string.Empty;
    public string? BestMatchAcceptance { get; set; }
    public string? BestMatchRemark { get; set; }
    public double? BaseScore { get; set; }
    public Dictionary<string, double> ScoreDetails { get; set; } = [];
    public string CurrentDecision { get; set; } = "manualReview";
    public List<string> EvidenceSummary { get; set; } = [];
    public List<string> ConflictSummary { get; set; } = [];
    public string? ReviewTrigger { get; set; }
    public int? LlmServiceId { get; set; }
    public LlmReviewScene ReviewScene { get; set; } = LlmReviewScene.MatchingReview;
}

/// <summary>
/// LLM 复核结果
/// </summary>
public class LlmReviewResult
{
    public double Score { get; set; }
    public string? Reason { get; set; }
    public string? Commentary { get; set; }
}

/// <summary>
/// LLM TopK 候选重排请求
/// </summary>
public class LlmCandidateRerankRequest
{
    public string SourceProject { get; set; } = string.Empty;
    public string SourceSpecification { get; set; } = string.Empty;
    public int CurrentTopCandidateSpecId { get; set; }
    public List<LlmCandidateRerankCandidate> Candidates { get; set; } = [];
    public int? LlmServiceId { get; set; }
}

/// <summary>
/// LLM TopK 候选重排中的候选项
/// </summary>
public class LlmCandidateRerankCandidate
{
    public int Rank { get; set; }
    public int SpecId { get; set; }
    public string Project { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public double EmbeddingScore { get; set; }
    public double FinalScore { get; set; }
    public Dictionary<string, double> ScoreDetails { get; set; } = [];
    public List<string> EvidenceSummary { get; set; } = [];
    public List<string> ConflictSummary { get; set; } = [];
}

/// <summary>
/// LLM TopK 候选重排结果
/// </summary>
public class LlmCandidateRerankResult
{
    public int SelectedSpecId { get; set; }
    public string? Reason { get; set; }
    public double Confidence { get; set; }
}
