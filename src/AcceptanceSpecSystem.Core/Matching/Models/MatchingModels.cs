namespace AcceptanceSpecSystem.Core.Matching.Models;

/// <summary>
/// 匹配结果
/// </summary>
public class MatchResult
{
    /// <summary>
    /// 源文本
    /// </summary>
    public string SourceText { get; set; } = string.Empty;

    /// <summary>
    /// 匹配到的目标文本
    /// </summary>
    public string MatchedText { get; set; } = string.Empty;

    /// <summary>
    /// 匹配的验收规格ID
    /// </summary>
    public int? MatchedSpecId { get; set; }

    /// <summary>
    /// 匹配的验收规格项目名称
    /// </summary>
    public string? MatchedProject { get; set; }

    /// <summary>
    /// 匹配的验收规格内容
    /// </summary>
    public string? MatchedSpecification { get; set; }

    /// <summary>
    /// 匹配的验收标准
    /// </summary>
    public string? MatchedAcceptance { get; set; }

    /// <summary>
    /// 综合相似度得分（0-1）
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// 各算法得分详情
    /// </summary>
    public Dictionary<string, double> ScoreDetails { get; set; } = [];

    /// <summary>
    /// LLM 复核得分（0-100，可选）
    /// </summary>
    public double? LlmScore { get; set; }

    /// <summary>
    /// LLM 复核原因（可选）
    /// </summary>
    public string? LlmReason { get; set; }

    /// <summary>
    /// LLM 复核评论（可选）
    /// </summary>
    public string? LlmCommentary { get; set; }

    /// <summary>
    /// 是否经过 LLM 复核
    /// </summary>
    public bool IsLlmReviewed => LlmScore.HasValue;

    /// <summary>
    /// 是否为高置信度匹配
    /// </summary>
    public bool IsHighConfidence => Score >= 0.8;

    /// <summary>
    /// 是否为中置信度匹配
    /// </summary>
    public bool IsMediumConfidence => Score >= 0.6 && Score < 0.8;

    /// <summary>
    /// 是否为低置信度匹配
    /// </summary>
    public bool IsLowConfidence => Score < 0.6;
}

/// <summary>
/// 匹配候选项
/// </summary>
public class MatchCandidate
{
    /// <summary>
    /// 验收规格ID
    /// </summary>
    public int SpecId { get; set; }

    /// <summary>
    /// 项目名称
    /// </summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// 规格内容
    /// </summary>
    public string Specification { get; set; } = string.Empty;

    /// <summary>
    /// 验收标准
    /// </summary>
    public string? Acceptance { get; set; }

    /// <summary>
    /// 用于匹配的组合文本
    /// </summary>
    public string CombinedText => $"{Project} {Specification}".Trim();

    /// <summary>
    /// Embedding向量（如果已计算）
    /// </summary>
    public float[]? Embedding { get; set; }
}

/// <summary>
/// 匹配配置
/// </summary>
public class MatchingConfig
{
    /// <summary>
    /// 使用的 Embedding 服务ID（为空则自动选择）
    /// </summary>
    public int? EmbeddingServiceId { get; set; }

    /// <summary>
    /// 使用的 LLM 服务ID（为空则自动选择）
    /// </summary>
    public int? LlmServiceId { get; set; }

    /// <summary>
    /// 最小匹配阈值
    /// </summary>
    public double MinScoreThreshold { get; set; } = 0.3;

    /// <summary>
    /// 是否启用 LLM 复核
    /// </summary>
    public bool UseLlmReview { get; set; } = false;

    /// <summary>
    /// 是否启用 LLM 生成建议
    /// </summary>
    public bool UseLlmSuggestion { get; set; } = false;

    /// <summary>
    /// 生成建议触发阈值（最佳得分低于该值）
    /// </summary>
    public double LlmSuggestionScoreThreshold { get; set; } = 0.6;
}

/// <summary>
/// 批量匹配请求
/// </summary>
public class BatchMatchRequest
{
    /// <summary>
    /// 待匹配的文本列表
    /// </summary>
    public List<string> SourceTexts { get; set; } = [];

    /// <summary>
    /// 目标制程ID（限定匹配范围）
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// 目标客户ID（限定匹配范围）
    /// </summary>
    public int? CustomerId { get; set; }

    /// <summary>
    /// 匹配配置
    /// </summary>
    public MatchingConfig Config { get; set; } = new();
}

/// <summary>
/// 批量匹配结果
/// </summary>
public class BatchMatchResult
{
    /// <summary>
    /// 匹配结果列表
    /// </summary>
    public List<MatchResult> Results { get; set; } = [];

    /// <summary>
    /// 总匹配数
    /// </summary>
    public int TotalMatched => Results.Count(r => r.MatchedSpecId.HasValue);

    /// <summary>
    /// 高置信度匹配数
    /// </summary>
    public int HighConfidenceCount => Results.Count(r => r.IsHighConfidence);

    /// <summary>
    /// 中置信度匹配数
    /// </summary>
    public int MediumConfidenceCount => Results.Count(r => r.IsMediumConfidence);

    /// <summary>
    /// 低置信度匹配数
    /// </summary>
    public int LowConfidenceCount => Results.Count(r => r.IsLowConfidence);
}
