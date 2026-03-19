namespace AcceptanceSpecSystem.Core.Matching.Models;

/// <summary>
/// 源匹配项
/// </summary>
public class MatchSource
{
    /// <summary>
    /// 项目名称
    /// </summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// 规格内容
    /// </summary>
    public string Specification { get; set; } = string.Empty;

    /// <summary>
    /// 组合文本
    /// </summary>
    public string CombinedText => $"{Project} {Specification}".Trim();
}

/// <summary>
/// 匹配策略
/// </summary>
public enum MatchingStrategy
{
    /// <summary>
    /// 单阶段匹配（当前默认行为）
    /// </summary>
    SingleStage = 1,

    /// <summary>
    /// 多阶段匹配（TopK召回 + 规则重排）
    /// </summary>
    MultiStage = 2
}

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
    /// 匹配的备注
    /// </summary>
    public string? MatchedRemark { get; set; }

    /// <summary>
    /// 综合相似度得分（0-1）
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Embedding 原始得分（0-1）
    /// </summary>
    public double EmbeddingScore { get; set; }

    /// <summary>
    /// 各算法得分详情
    /// </summary>
    public Dictionary<string, double> ScoreDetails { get; set; } = [];

    /// <summary>
    /// 匹配策略
    /// </summary>
    public MatchingStrategy MatchingStrategy { get; set; } = MatchingStrategy.SingleStage;

    /// <summary>
    /// 第一阶段召回候选数
    /// </summary>
    public int RecalledCandidateCount { get; set; }

    /// <summary>
    /// 是否为高歧义样本
    /// </summary>
    public bool IsAmbiguous { get; set; }

    /// <summary>
    /// Top1 与 Top2 的最终分差（可选）
    /// </summary>
    public double? ScoreGap { get; set; }

    /// <summary>
    /// 重排摘要（可选）
    /// </summary>
    public string? RerankSummary { get; set; }

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

    /// <summary>
    /// 是否为降级结果（Embedding 不可用时回退到文本相似度）
    /// </summary>
    public bool IsDegraded { get; set; }

    /// <summary>
    /// 降级原因说明
    /// </summary>
    public string? DegradationReason { get; set; }
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
    /// 备注
    /// </summary>
    public string? Remark { get; set; }

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
    /// 匹配策略
    /// </summary>
    public MatchingStrategy MatchingStrategy { get; set; } = MatchingStrategy.SingleStage;

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
    /// 多阶段模式下第一阶段召回数量
    /// </summary>
    public int RecallTopK { get; set; } = 5;

    /// <summary>
    /// 多阶段模式下的歧义分差阈值
    /// </summary>
    public double AmbiguityMargin { get; set; } = 0.03;

    /// <summary>
    /// 是否启用 LLM 复核
    /// </summary>
    public bool UseLlmReview { get; set; } = false;

    /// <summary>
    /// 是否启用 LLM 生成建议
    /// </summary>
    public bool UseLlmSuggestion { get; set; } = false;

    /// <summary>
    /// 是否对完全无匹配的行也生成建议
    /// </summary>
    public bool SuggestNoMatchRows { get; set; } = false;

    /// <summary>
    /// 生成建议触发阈值（最佳得分低于该值）
    /// </summary>
    public double LlmSuggestionScoreThreshold { get; set; } = 0.6;

    /// <summary>
    /// LLM 并行处理数（1~10，默认3）
    /// </summary>
    public int LlmParallelism { get; set; } = 3;

    /// <summary>
    /// LLM 单行处理超时时间（秒，默认45）
    /// </summary>
    public int LlmRowTimeoutSeconds { get; set; } = 45;

    /// <summary>
    /// LLM 单行失败重试次数（默认1）
    /// </summary>
    public int LlmRetryCount { get; set; } = 1;

    /// <summary>
    /// LLM 熔断阈值（累计失败次数达到后停止新任务，默认10）
    /// </summary>
    public int LlmCircuitBreakFailures { get; set; } = 10;

    /// <summary>
    /// 是否过滤项目列与规格列都为空的源行（默认过滤）
    /// </summary>
    public bool FilterEmptySourceRows { get; set; } = true;
}

/// <summary>
/// 批量匹配请求
/// </summary>
public class BatchMatchRequest
{
    /// <summary>
    /// 待匹配的源项列表
    /// </summary>
    public List<MatchSource> SourceItems { get; set; } = [];

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

    /// <summary>
    /// 高歧义样本数
    /// </summary>
    public int AmbiguousCount => Results.Count(r => r.IsAmbiguous);
}
