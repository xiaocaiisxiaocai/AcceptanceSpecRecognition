namespace AcceptanceSpecSystem.Core.Matching.Models;

/// <summary>
/// 匹配阈值约定
/// </summary>
public static class MatchingThresholds
{
    /// <summary>
    /// 默认最小匹配阈值
    /// </summary>
    public const double DefaultMinScoreThreshold = 0.90;

    /// <summary>
    /// 默认高置信结果分层阈值
    /// </summary>
    public const double DefaultHighConfidenceScore = 0.95;

    /// <summary>
    /// 默认召回候选数
    /// </summary>
    public const int DefaultRecallTopK = 2;

    /// <summary>
    /// 召回候选数上限
    /// </summary>
    public const int MaxRecallTopK = 3;

    /// <summary>
    /// 默认歧义分差阈值
    /// </summary>
    public const double DefaultAmbiguityMargin = 0.02;

    /// <summary>
    /// 中置信度下限
    /// </summary>
    public const double MediumConfidenceScore = 0.6;

    /// <summary>
    /// LLM 复核通过阈值（0~100）
    /// </summary>
    public const double LlmReviewPassScore = 90;

    /// <summary>
    /// LLM 等价裁决置信度下限（默认 0.5）。
    /// LLM 判定 Equivalent 但置信度低于此值时，视为不确定，转人工确认。
    /// 0 表示不设门槛（任何置信度均接受），1 表示要求 LLM 完全确定。
    /// </summary>
    public const double DefaultLlmEquivalenceMinConfidence = 0.5;

    /// <summary>
    /// 存在型号/料号冲突时，LLM Equivalent 结论放行所需的最低置信度。
    /// 错填物料号是验收场景最危险的错误，任何模式（含语义优先）下都按此更高门槛把关。
    /// </summary>
    public const double IdentifierConflictEquivalenceMinConfidence = 0.85;

    /// <summary>
    /// LLM 判定等价且已自动通过时，展示为"高置信"所需的 LLM 自评置信度下限。
    /// 低于此值的等价自动通过仍会填充，但前端标注为"中置信"，供审核员优先快速复查。
    /// 仅影响展示分级（high/medium），不改变是否自动填充。
    /// </summary>
    public const double HighConfidenceLlmEquivalenceMinConfidence = 0.85;

    /// <summary>
    /// 归一化高置信阈值配置。
    /// </summary>
    public static double NormalizeHighConfidenceThreshold(double? threshold)
    {
        return Math.Clamp(
            threshold ?? DefaultHighConfidenceScore,
            0.5,
            1);
    }
}

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
    /// 结构化匹配证据
    /// </summary>
    public MatchEvidence Evidence { get; set; } = new();

    /// <summary>
    /// 结构化问题列表
    /// </summary>
    public List<MatchIssue> Issues { get; set; } = [];

    /// <summary>
    /// 用于详情展示的Top候选列表（含Top1）
    /// </summary>
    public List<MatchCandidateSnapshot> TopCandidates { get; set; } = [];

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
    /// 当前最佳候选的选中方式
    /// </summary>
    public MatchSelectionMode SelectionMode { get; set; } = MatchSelectionMode.EmbeddingTop1;

    /// <summary>
    /// 当前最佳候选的选中摘要
    /// </summary>
    public string? SelectionSummary { get; set; }

    /// <summary>
    /// 当前结果的匹配依据
    /// </summary>
    public MatchBasis MatchBasis { get; set; } = MatchBasis.ProjectSpecification;

    /// <summary>
    /// AI 等价裁决结果
    /// </summary>
    public LlmEquivalenceAdjudicationResult? LlmEquivalence { get; set; }

    /// <summary>
    /// 最终决策。默认人工确认（fail-safe），由各匹配路径显式升级为 AutoApply。
    /// </summary>
    public MatchDecision Decision { get; set; } = MatchDecision.ManualReview;

    /// <summary>
    /// 本次匹配使用的最小得分阈值
    /// </summary>
    public double MinScoreThreshold { get; set; } = MatchingThresholds.DefaultMinScoreThreshold;

    /// <summary>
    /// 本次匹配使用的高置信阈值
    /// </summary>
    public double HighConfidenceThreshold { get; set; } = MatchingThresholds.DefaultHighConfidenceScore;

    /// <summary>
    /// 本次匹配使用的 LLM 等价裁决置信度下限
    /// </summary>
    public double LlmEquivalenceMinConfidence { get; set; } = MatchingThresholds.DefaultLlmEquivalenceMinConfidence;

    /// <summary>
    /// 是否为高置信度匹配。
    /// LLM 判定等价时，需置信度不低于 <see cref="LlmEquivalenceMinConfidence"/> 才算高置信。
    /// </summary>
    public bool IsHighConfidence =>
        Decision == MatchDecision.AutoApply &&
        (Score >= HighConfidenceThreshold ||
         (LlmEquivalence?.Verdict == LlmEquivalenceVerdict.Equivalent &&
          (LlmEquivalenceMinConfidence <= 0 || LlmEquivalence.Confidence >= LlmEquivalenceMinConfidence)));

    /// <summary>
    /// 是否为中置信度匹配
    /// </summary>
    public bool IsMediumConfidence =>
        Decision == MatchDecision.AutoApply &&
        LlmEquivalence?.Verdict != LlmEquivalenceVerdict.Equivalent &&
        Score >= MinScoreThreshold &&
        Score < HighConfidenceThreshold;

    /// <summary>
    /// 是否为低置信度匹配
    /// </summary>
    public bool IsLowConfidence =>
        Decision == MatchDecision.AutoApply &&
        LlmEquivalence?.Verdict != LlmEquivalenceVerdict.Equivalent &&
        Score < MinScoreThreshold;
}

/// <summary>
/// 匹配结果详情中的候选快照
/// </summary>
public class MatchCandidateSnapshot
{
    /// <summary>
    /// 候选排名（从1开始）
    /// </summary>
    public int Rank { get; set; }

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
    /// 当前候选得分
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Embedding 原始得分
    /// </summary>
    public double EmbeddingScore { get; set; }

    /// <summary>
    /// 各算法得分详情
    /// </summary>
    public Dictionary<string, double> ScoreDetails { get; set; } = [];

    /// <summary>
    /// 当前候选的结构化证据
    /// </summary>
    public MatchEvidence Evidence { get; set; } = new();

    /// <summary>
    /// 当前候选的结构化问题列表
    /// </summary>
    public List<MatchIssue> Issues { get; set; } = [];

    /// <summary>
    /// 重排摘要
    /// </summary>
    public string? RerankSummary { get; set; }

    /// <summary>
    /// 该候选在当前结果中的选中方式
    /// </summary>
    public MatchSelectionMode SelectionMode { get; set; } = MatchSelectionMode.EmbeddingTop1;

    /// <summary>
    /// 该候选在当前结果中的选中摘要
    /// </summary>
    public string? SelectionSummary { get; set; }

    /// <summary>
    /// 该候选在当前结果中的匹配依据
    /// </summary>
    public MatchBasis MatchBasis { get; set; } = MatchBasis.ProjectSpecification;

    /// <summary>
    /// AI 等价裁决结果（仅 Top1 或参与裁决候选可用）
    /// </summary>
    public LlmEquivalenceAdjudicationResult? LlmEquivalence { get; set; }
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

public enum MatchSelectionMode
{
    ExactShortcut = 1,
    EmbeddingTop1 = 2,
    AiRerank = 3
}

public enum MatchingMode
{
    ProjectSpecification = 1,
    SpecificationOnly = 2
}

public enum MatchBasis
{
    ProjectSpecification = 1,
    Specification = 2
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
    public double MinScoreThreshold { get; set; } = MatchingThresholds.DefaultMinScoreThreshold;

    /// <summary>
    /// 第一阶段召回数量
    /// </summary>
    public int RecallTopK { get; set; } = MatchingThresholds.DefaultRecallTopK;

    /// <summary>
    /// 歧义分差阈值
    /// </summary>
    public double AmbiguityMargin { get; set; } = MatchingThresholds.DefaultAmbiguityMargin;

    /// <summary>
    /// 高置信结果分层阈值
    /// </summary>
    public double HighConfidenceThreshold { get; set; } = MatchingThresholds.DefaultHighConfidenceScore;

    /// <summary>
    /// LLM 并行处理数（1~10，默认4）
    /// </summary>
    public int LlmParallelism { get; set; } = 4;

    /// <summary>
    /// LLM 单行处理超时时间（秒，默认120）
    /// </summary>
    public int LlmRowTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// LLM 单行失败重试次数（默认1）
    /// </summary>
    public int LlmRetryCount { get; set; } = 1;

    /// <summary>
    /// LLM 熔断阈值（累计失败次数达到后停止新任务，默认10）
    /// </summary>
    public int LlmCircuitBreakFailures { get; set; } = 10;

    /// <summary>
    /// 匹配方式，默认项目+规格。
    /// </summary>
    public MatchingMode MatchingMode { get; set; } = MatchingMode.ProjectSpecification;

    /// <summary>
    /// 是否在同步匹配阶段启用 LLM 等价裁决。
    /// 注意：启用后 LLM 仅作为"确定性层无法判定的灰区"兜底，且受 <see cref="LlmMaxCallsPerBatch"/> 全局上限约束，
    /// 不再对每个非精确命中行逐一调用。
    /// </summary>
    public bool EnableLlmEquivalenceAdjudication { get; set; } = true;

    /// <summary>
    /// LLM 等价裁决置信度下限（0~1）。
    /// LLM 判定 Equivalent 但自评置信度低于此值时，视为 Uncertain，转人工确认。
    /// 默认 0.5，设为 0 表示不设门槛（任何置信度均接受）。
    /// </summary>
    public double LlmEquivalenceMinConfidence { get; set; } = MatchingThresholds.DefaultLlmEquivalenceMinConfidence;

    /// <summary>
    /// 是否启用确定性自动通过路径。
    /// 开启后：无硬冲突 + Embedding 达到高置信阈值 + 不歧义的行可直接 AutoApply，无需 LLM 裁决。
    /// 默认开启，是将 LLM 移出匹配热路径的核心开关。
    /// </summary>
    public bool EnableDeterministicAutoApply { get; set; } = true;

    /// <summary>
    /// 单批次 LLM 等价裁决调用次数上限（限流兜底）。
    /// 达到上限后，剩余需要裁决的灰区行一律转为人工确认，避免大批量时 LLM 拖垮整体耗时。
    /// 默认 1000（本地部署无费用限制）。设为 0 表示不允许任何 LLM 裁决调用。
    /// </summary>
    public int LlmMaxCallsPerBatch { get; set; } = 1000;

    /// <summary>
    /// 是否仅按项目+规格完全一致命中
    /// </summary>
    public bool ExactMatchOnly { get; set; }

    /// <summary>
    /// 是否过滤项目列与规格列都为空的源行（默认过滤）
    /// </summary>
    public bool FilterEmptySourceRows { get; set; } = true;

    /// <summary>
    /// 启用 LLM 语义优先模式。
    /// 开启后 LLM 等价裁决具有最高权威：
    /// 1. 召回门槛自动降低至 <see cref="LlmSemanticRecallThreshold"/>，让 Embedding 偏低的语义候选也能进入 LLM 视野；
    /// 2. 未知单位/品牌 Warning 不再阻断 LLM Equivalent 结论；
    /// 3. 确定性硬冲突规则降级，LLM Equivalent 可放行。
    /// 注意：速度会明显变慢，适合对准确率要求高、可接受较长等待的场景。
    /// </summary>
    public bool EnableLlmSemanticPriority { get; set; }

    /// <summary>
    /// LLM 语义优先模式下的召回分数下限（默认 0.5）。
    /// 仅在 <see cref="EnableLlmSemanticPriority"/> 开启时生效。
    /// Embedding 分数 >= 此阈值的候选将被保留进入 LLM 裁决视野，即使低于常规 MinScoreThreshold。
    /// </summary>
    public double LlmSemanticRecallThreshold { get; set; } = 0.5;
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

/// <summary>
/// 批量匹配进度快照。
/// </summary>
public sealed class BatchMatchProgress
{
    /// <summary>
    /// 当前阶段标识。
    /// </summary>
    public string Stage { get; set; } = "matching";

    /// <summary>
    /// 当前阶段文案。
    /// </summary>
    public string? StageText { get; set; }

    /// <summary>
    /// 阶段补充说明。
    /// </summary>
    public string? DetailText { get; set; }

    /// <summary>
    /// 已完成的行数。
    /// </summary>
    public int CompletedItems { get; set; }

    /// <summary>
    /// 总行数。
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// LLM 等价裁决已调用次数（0 表示未启用或尚未调用）。
    /// </summary>
    public int LlmCallsUsed { get; set; }

    /// <summary>
    /// LLM 等价裁决总预算（0 表示未启用）。
    /// </summary>
    public int LlmCallsBudget { get; set; }

    /// <summary>
    /// LLM 预算是否已耗尽。
    /// </summary>
    public bool LlmBudgetExhausted { get; set; }
}
