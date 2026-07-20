using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Contracts;

/// <summary>
/// 待匹配的源项
/// </summary>
public class MatchSourceItem
{
    public string? RegionId { get; set; }

    public int? RegionIndex { get; set; }

    public int? AcceptanceColumnIndex { get; set; }

    public int? RemarkColumnIndex { get; set; }

    /// <summary>
    /// 行索引（用于定位）
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// 项目名称
    /// </summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// 规格内容
    /// </summary>
    public string Specification { get; set; } = string.Empty;

    /// <summary>
    /// 组合文本（用于匹配）
    /// </summary>
    public string CombinedText => $"{Project} {Specification}".Trim();
}

/// <summary>
/// 匹配配置DTO
/// </summary>
public class MatchConfigDto
{
    /// <summary>
    /// 选定的 Embedding 服务ID（为空则自动选择）
    /// </summary>
    public int? EmbeddingServiceId { get; set; }

    /// <summary>
    /// 选定的 LLM 服务ID（为空则自动选择）
    /// </summary>
    public int? LlmServiceId { get; set; }

    /// <summary>
    /// 最小匹配阈值
    /// </summary>
    public double MinScoreThreshold { get; set; } = MatchingThresholds.DefaultMinScoreThreshold;

    /// <summary>
    /// 第一阶段召回数量
    /// </summary>
    public int? RecallTopK { get; set; }

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
    /// 匹配方式：projectSpecification=项目+规格，specificationOnly=仅规格。
    /// </summary>
    public string? MatchingMode { get; set; }

    /// <summary>
    /// 是否在同步匹配阶段启用 AI 等价裁决（默认开启，利用 LLM 语义理解能力裁决灰区行）。
    /// </summary>
    public bool EnableLlmEquivalenceAdjudication { get; set; } = true;

    /// <summary>
    /// LLM 等价裁决置信度下限（0~1）。
    /// LLM 判定等价但自评置信度低于此值时，转人工确认。默认 0.5，设为 0 表示不设门槛。
    /// </summary>
    public double LlmEquivalenceMinConfidence { get; set; } = MatchingThresholds.DefaultLlmEquivalenceMinConfidence;

    /// <summary>
    /// 是否启用确定性自动通过（无硬冲突且高置信时跳过 LLM 直接采用）。
    /// </summary>
    public bool EnableDeterministicAutoApply { get; set; } = true;

    /// <summary>
    /// 单批次 LLM 重排/等价裁决调用次数上限（本地部署无费用限制，默认 200）。
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
    /// 启用 LLM 语义优先模式（默认关闭）。
    /// 开启后 LLM 等价裁决具有最高权威，覆盖确定性冲突规则，速度变慢但命中率更高。
    /// </summary>
    public bool EnableLlmSemanticPriority { get; set; }

    /// <summary>
    /// LLM 语义优先模式下的召回分数下限（默认 0.5）。
    /// 仅在 EnableLlmSemanticPriority 开启时生效。
    /// </summary>
    public double LlmSemanticRecallThreshold { get; set; } = 0.5;

    /// <summary>
    /// 高 Embedding 语义自动通过阈值（0~1，默认 0=关闭）。
    /// </summary>
    public double EmbeddingSemanticAutoApplyThreshold { get; set; }
}

/// <summary>
/// 匹配预览项
/// </summary>
public class MatchPreviewItem
{
    public string? RegionId { get; set; }

    public int? RegionIndex { get; set; }

    public int? AcceptanceColumnIndex { get; set; }

    public int? RemarkColumnIndex { get; set; }

    /// <summary>
    /// 行索引
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// 源项目名称
    /// </summary>
    public string SourceProject { get; set; } = string.Empty;

    /// <summary>
    /// 源规格内容
    /// </summary>
    public string SourceSpecification { get; set; } = string.Empty;

    /// <summary>
    /// 最佳匹配结果
    /// </summary>
    public MatchResultDto? BestMatch { get; set; }

    /// <summary>
    /// 不匹配原因
    /// </summary>
    public string? NoMatchReason { get; set; }

    /// <summary>
    /// 是否有匹配
    /// </summary>
    public bool HasMatch => BestMatch != null;

    /// <summary>
    /// 置信度级别
    /// </summary>
    public string ConfidenceLevel { get; set; } = "none";
}

/// <summary>
/// 匹配结果DTO
/// </summary>
public class MatchResultDto
{
    /// <summary>
    /// 匹配的验收规格ID
    /// </summary>
    public int SpecId { get; set; }

    /// <summary>
    /// 匹配的项目名称
    /// </summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// 匹配的规格内容
    /// </summary>
    public string Specification { get; set; } = string.Empty;

    /// <summary>
    /// 匹配的验收标准
    /// </summary>
    public string? Acceptance { get; set; }

    /// <summary>
    /// 匹配的备注
    /// </summary>
    public string? Remark { get; set; }

    /// <summary>
    /// 综合得分（0-1）
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
    /// 最终决策
    /// </summary>
    public string Decision { get; set; } = "autoApply";

    /// <summary>
    /// 证据摘要
    /// </summary>
    public List<string> EvidenceSummary { get; set; } = [];

    /// <summary>
    /// 冲突摘要
    /// </summary>
    public List<string> ConflictSummary { get; set; } = [];

    /// <summary>
    /// 结构化问题列表
    /// </summary>
    public List<MatchIssueDto> Issues { get; set; } = [];

    /// <summary>
    /// 实体证据列表
    /// </summary>
    public List<MatchEntityEvidenceDto> Entities { get; set; } = [];

    /// <summary>
    /// Top候选列表（含Top1）
    /// </summary>
    public List<MatchCandidateDto> TopCandidates { get; set; } = [];

    /// <summary>
    /// 第一阶段召回候选数
    /// </summary>
    public int RecalledCandidateCount { get; set; }

    /// <summary>
    /// 是否为高歧义样本
    /// </summary>
    public bool IsAmbiguous { get; set; }

    /// <summary>
    /// Top1 与 Top2 的最终分差
    /// </summary>
    public double? ScoreGap { get; set; }

    /// <summary>
    /// 重排摘要
    /// </summary>
    public string? RerankSummary { get; set; }

    /// <summary>
    /// 当前最佳候选的选中方式
    /// </summary>
    public string SelectionMode { get; set; } = "embeddingTop1";

    /// <summary>
    /// 当前最佳候选的选中摘要
    /// </summary>
    public string? SelectionSummary { get; set; }

    /// <summary>
    /// 匹配依据
    /// </summary>
    public string MatchBasis { get; set; } = "projectSpecification";

    /// <summary>
    /// AI 等价裁决结果
    /// </summary>
    public LlmEquivalenceDto? LlmEquivalence { get; set; }

    /// <summary>
    /// 服务端签发的预览放行令牌
    /// </summary>
    public string? ReviewApprovalToken { get; set; }

    /// <summary>
    /// AI 复核分
    /// </summary>
    public double? ReviewScore { get; set; }

    /// <summary>
    /// AI 复核原因
    /// </summary>
    public string? ReviewReason { get; set; }

    /// <summary>
    /// AI 复核说明
    /// </summary>
    public string? ReviewCommentary { get; set; }
}

/// <summary>
/// 匹配详情中的候选项
/// </summary>
public class MatchCandidateDto
{
    /// <summary>
    /// 候选排名（从1开始）
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// 规格ID
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
    /// 最终决策
    /// </summary>
    public string Decision { get; set; } = "manualReview";

    /// <summary>
    /// 证据摘要
    /// </summary>
    public List<string> EvidenceSummary { get; set; } = [];

    /// <summary>
    /// 冲突摘要
    /// </summary>
    public List<string> ConflictSummary { get; set; } = [];

    /// <summary>
    /// 结构化问题列表
    /// </summary>
    public List<MatchIssueDto> Issues { get; set; } = [];

    /// <summary>
    /// 实体证据列表
    /// </summary>
    public List<MatchEntityEvidenceDto> Entities { get; set; } = [];

    /// <summary>
    /// 重排摘要
    /// </summary>
    public string? RerankSummary { get; set; }

    /// <summary>
    /// 当前候选的选中方式
    /// </summary>
    public string SelectionMode { get; set; } = "embeddingTop1";

    /// <summary>
    /// 当前候选的选中摘要
    /// </summary>
    public string? SelectionSummary { get; set; }

    /// <summary>
    /// 匹配依据
    /// </summary>
    public string MatchBasis { get; set; } = "projectSpecification";

    /// <summary>
    /// AI 等价裁决结果
    /// </summary>
    public LlmEquivalenceDto? LlmEquivalence { get; set; }
}

/// <summary>
/// AI 等价裁决 DTO
/// </summary>
public class LlmEquivalenceDto
{
    /// <summary>
    /// 裁决结论
    /// </summary>
    public string Verdict { get; set; } = "uncertain";

    /// <summary>
    /// 原因类型
    /// </summary>
    public string ReasonType { get; set; } = "uncertain";

    /// <summary>
    /// 中文原因说明
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// 置信度（0~1）
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// 实体证据
/// </summary>
public class MatchEntityEvidenceDto
{
    /// <summary>
    /// 实体类型
    /// </summary>
    public string EntityType { get; set; } = "品牌";

    /// <summary>
    /// 源值
    /// </summary>
    public string SourceValue { get; set; } = string.Empty;

    /// <summary>
    /// 候选值
    /// </summary>
    public string CandidateValue { get; set; } = string.Empty;

    /// <summary>
    /// 源归一化值
    /// </summary>
    public string NormalizedSourceValue { get; set; } = string.Empty;

    /// <summary>
    /// 候选归一化值
    /// </summary>
    public string NormalizedCandidateValue { get; set; } = string.Empty;

    /// <summary>
    /// 证据关系
    /// </summary>
    public string Relation { get; set; } = "unknown";
}

/// <summary>
/// 匹配问题说明
/// </summary>
public class MatchIssueDto
{
    /// <summary>
    /// 问题编码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 严重级别
    /// </summary>
    public string Severity { get; set; } = "warning";

    /// <summary>
    /// 问题所属字段
    /// </summary>
    public string? FieldName { get; set; }

    /// <summary>
    /// 源值
    /// </summary>
    public string? SourceValue { get; set; }

    /// <summary>
    /// 候选值
    /// </summary>
    public string? CandidateValue { get; set; }

    /// <summary>
    /// 用户说明
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 建议动作
    /// </summary>
    public string? SuggestedAction { get; set; }
}

/// <summary>
/// 填充映射
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class FillMapping
{
    /// <summary>
    /// 行索引
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// 选择的验收规格ID
    /// </summary>
    public int? SpecId { get; set; }

    /// <summary>
    /// 是否已由用户人工确认
    /// </summary>
    public bool ManualConfirmed { get; set; }

    /// <summary>
    /// 无匹配规格时，是否使用本次提交的手工填写值
    /// </summary>
    public bool ManualFill { get; set; }

    /// <summary>
    /// 服务端签发的复核放行令牌
    /// </summary>
    public string? ReviewApprovalToken { get; set; }

    /// <summary>
    /// 本次导出的验收标准覆盖值
    /// </summary>
    public string? OverrideAcceptance { get; set; }

    /// <summary>
    /// 本次导出的备注覆盖值
    /// </summary>
    public string? OverrideRemark { get; set; }
}

/// <summary>
/// 执行填充响应
/// </summary>
public class ExecuteFillResponse
{
    /// <summary>
    /// 填充任务ID
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// 填充成功数量
    /// </summary>
    public int FilledCount { get; set; }

    /// <summary>
    /// 跳过数量
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// 下载URL
    /// </summary>
    public string DownloadUrl { get; set; } = string.Empty;
}

/// <summary>
/// LLM 流式请求
/// </summary>
public class MatchLlmStreamRequest
{
    /// <summary>
    /// 目标客户ID（限定匹配范围）
    /// </summary>
    public int? CustomerId { get; set; }

    /// <summary>
    /// 目标制程ID（限定匹配范围）
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// 目标机型ID（限定匹配范围）
    /// </summary>
    public int? MachineModelId { get; set; }

    /// <summary>
    /// 待复核/生成的行列表
    /// </summary>
    public List<MatchLlmStreamItem> Items { get; set; } = [];

    /// <summary>
    /// 匹配配置
    /// </summary>
    public MatchConfigDto? Config { get; set; }
}

/// <summary>
/// LLM 流式请求项
/// </summary>
public class MatchLlmStreamItem
{
    /// <summary>
    /// 表格索引（多表场景下用于唯一定位）
    /// </summary>
    public int? TableIndex { get; set; }

    /// <summary>
    /// 行索引
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// 源项目名称
    /// </summary>
    public string SourceProject { get; set; } = string.Empty;

    /// <summary>
    /// 源规格内容
    /// </summary>
    public string SourceSpecification { get; set; } = string.Empty;

    /// <summary>
    /// 最佳匹配的验收规格ID
    /// </summary>
    public int? BestMatchSpecId { get; set; }

    /// <summary>
    /// 最佳匹配基础得分
    /// </summary>
    public double? BestMatchScore { get; set; }

    /// <summary>
    /// 算法得分明细
    /// </summary>
    public Dictionary<string, double>? ScoreDetails { get; set; }

    /// <summary>
    /// 当前决策（autoApply/manualReview/reject）
    /// </summary>
    public string? Decision { get; set; }

    /// <summary>
    /// AI 等价裁决结论
    /// </summary>
    public string? LlmEquivalenceVerdict { get; set; }

    /// <summary>
    /// 是否为高歧义样本
    /// </summary>
    public bool IsAmbiguous { get; set; }

    /// <summary>
    /// 证据摘要
    /// </summary>
    public List<string>? EvidenceSummary { get; set; }

    /// <summary>
    /// 冲突摘要
    /// </summary>
    public List<string>? ConflictSummary { get; set; }
}

// ===== 批量填充 DTO =====

/// <summary>
/// 批量表格配置（单个表格的列索引）
/// </summary>
public class BatchTableRegionConfig
{
    public string? RegionId { get; set; }
    public int RegionIndex { get; set; }
    public int ProjectColumnIndex { get; set; }
    public int SpecificationColumnIndex { get; set; }
    public int AcceptanceColumnIndex { get; set; }
    public int? RemarkColumnIndex { get; set; }
    public int? HeaderRowStart { get; set; }
    public int? HeaderRowCount { get; set; }
    public int? DataStartRow { get; set; }
    public int? DataEndRow { get; set; }
}
public class BatchTableConfig
{
    /// <summary>
    /// 表格索引
    /// </summary>
    public int TableIndex { get; set; }

    /// <summary>
    /// 批量回复目标表对应的来源表索引（可选；未传时默认与目标表索引一致）
    /// </summary>
    public int? SourceTableIndex { get; set; }

    /// <summary>
    /// 项目列索引
    /// </summary>
    public int ProjectColumnIndex { get; set; }

    /// <summary>
    /// 规格列索引
    /// </summary>
    public int SpecificationColumnIndex { get; set; }

    /// <summary>
    /// 验收列索引
    /// </summary>
    public int AcceptanceColumnIndex { get; set; }

    /// <summary>
    /// 备注列索引（可选）
    /// </summary>
    public int? RemarkColumnIndex { get; set; }

    /// <summary>
    /// Excel 表头起始行（1-based，可选；未传则默认已用区域首行）
    /// </summary>
    public int? HeaderRowStart { get; set; }

    /// <summary>
    /// Excel 表头行数（可选；未传默认 1）
    /// </summary>
    public int? HeaderRowCount { get; set; }

    /// <summary>
    /// Excel 数据起始行（1-based，可选；未传则默认紧随表头）
    /// </summary>
    public int? DataStartRow { get; set; }

    public int? DataEndRow { get; set; }

    public List<BatchTableRegionConfig> Regions { get; set; } = [];

    /// <summary>
    /// 是否过滤项目列与规格列都为空的源行（表格级，可选；未传时走全局配置）
    /// </summary>
    public bool? FilterEmptySourceRows { get; set; }

    /// <summary>
    /// 重复项目/规格组合处理决议
    /// </summary>
    public List<BatchReplyDuplicateResolutionDto> DuplicateResolutions { get; set; } = [];
}

/// <summary>
/// 批量预览请求
/// </summary>
public class BatchPreviewRequest
{
    /// <summary>
    /// 文件ID
    /// </summary>
    public int FileId { get; set; }

    /// <summary>
    /// 前端生成的预览请求ID，用于轮询进度。
    /// </summary>
    public string? PreviewRequestId { get; set; }

    /// <summary>
    /// 各表格配置列表
    /// </summary>
    public List<BatchTableConfig> Tables { get; set; } = [];

    /// <summary>
    /// 匹配范围：客户ID
    /// </summary>
    public int? CustomerId { get; set; }

    /// <summary>
    /// 匹配范围：制程ID
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// 匹配范围：机型ID
    /// </summary>
    public int? MachineModelId { get; set; }

    /// <summary>
    /// 匹配配置
    /// </summary>
    public MatchConfigDto? Config { get; set; }
}

/// <summary>
/// 单个表格的批量预览结果
/// </summary>
public class BatchTablePreviewResult
{
    /// <summary>
    /// 表格索引
    /// </summary>
    public int TableIndex { get; set; }

    /// <summary>
    /// 匹配结果列表
    /// </summary>
    public List<MatchPreviewItem> Items { get; set; } = [];

    /// <summary>
    /// 总匹配数
    /// </summary>
    public int TotalMatched { get; set; }

    /// <summary>
    /// 高置信度匹配数
    /// </summary>
    public int HighConfidenceCount { get; set; }

    /// <summary>
    /// 中置信度匹配数
    /// </summary>
    public int MediumConfidenceCount { get; set; }

    /// <summary>
    /// 低置信度匹配数
    /// </summary>
    public int LowConfidenceCount { get; set; }

    /// <summary>
    /// 高歧义样本数
    /// </summary>
    public int AmbiguousCount { get; set; }
}

/// <summary>
/// 执行记录归档用的预览表格快照
/// </summary>
public class ExecutionHistoryPreviewTableSnapshot
{
    /// <summary>
    /// 表格索引
    /// </summary>
    public int TableIndex { get; set; }

    /// <summary>
    /// 预览项列表
    /// </summary>
    public List<MatchPreviewItem> Items { get; set; } = [];
}

/// <summary>
/// 批量预览响应
/// </summary>
public class BatchPreviewResponse
{
    /// <summary>
    /// 各表格预览结果
    /// </summary>
    public List<BatchTablePreviewResult> Tables { get; set; } = [];

    /// <summary>
    /// 汇总：总匹配数
    /// </summary>
    public int TotalMatched => Tables.Sum(t => t.TotalMatched);

    /// <summary>
    /// 汇总：高置信度
    /// </summary>
    public int HighConfidenceCount => Tables.Sum(t => t.HighConfidenceCount);

    /// <summary>
    /// 汇总：中置信度
    /// </summary>
    public int MediumConfidenceCount => Tables.Sum(t => t.MediumConfidenceCount);

    /// <summary>
    /// 汇总：低置信度
    /// </summary>
    public int LowConfidenceCount => Tables.Sum(t => t.LowConfidenceCount);

    /// <summary>
    /// 汇总：高歧义样本
    /// </summary>
    public int AmbiguousCount => Tables.Sum(t => t.AmbiguousCount);
}

/// <summary>
/// 批量预览进度响应。
/// </summary>
public class BatchPreviewProgressResponse
{
    /// <summary>
    /// 预览请求ID。
    /// </summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// 当前状态（running/completed/failed）。
    /// </summary>
    public string Status { get; set; } = "running";

    /// <summary>
    /// 当前阶段标识。
    /// </summary>
    public string Stage { get; set; } = "preparing";

    /// <summary>
    /// 当前阶段说明。
    /// </summary>
    public string StageText { get; set; } = string.Empty;

    /// <summary>
    /// 阶段补充说明。
    /// </summary>
    public string? DetailText { get; set; }

    /// <summary>
    /// 已完成条数。
    /// </summary>
    public int CompletedItems { get; set; }

    /// <summary>
    /// 总条数。
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// 进度百分比（0~100）。
    /// </summary>
    public double ProgressPercent { get; set; }

    /// <summary>
    /// 开始时间（UTC）。
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// 最后更新时间（UTC）。
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 已耗时毫秒。
    /// </summary>
    public long ElapsedMs { get; set; }
}

/// <summary>
/// 批量填充映射（按表格分组）
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class BatchTableFillMapping
{
    /// <summary>
    /// 表格索引
    /// </summary>
    public int TableIndex { get; set; }

    /// <summary>
    /// 验收列索引
    /// </summary>
    public int AcceptanceColumnIndex { get; set; }

    /// <summary>
    /// 备注列索引（可选）
    /// </summary>
    public int? RemarkColumnIndex { get; set; }

    /// <summary>
    /// 项目列索引（执行前重算当前最佳匹配必填）
    /// </summary>
    public int? ProjectColumnIndex { get; set; }

    /// <summary>
    /// 规格列索引（执行前重算当前最佳匹配必填）
    /// </summary>
    public int? SpecificationColumnIndex { get; set; }

    /// <summary>
    /// Excel 表头起始行（1-based，可选）
    /// </summary>
    public int? HeaderRowStart { get; set; }

    /// <summary>
    /// Excel 表头行数（可选）
    /// </summary>
    public int? HeaderRowCount { get; set; }

    /// <summary>
    /// Excel 数据起始行（1-based，可选）
    /// </summary>
    public int? DataStartRow { get; set; }

    public int? DataEndRow { get; set; }

    public List<BatchTableRegionConfig> Regions { get; set; } = [];

    /// <summary>
    /// 是否过滤项目列与规格列都为空的行
    /// </summary>
    public bool? FilterEmptySourceRows { get; set; }

    /// <summary>
    /// 该表格的填充映射列表
    /// </summary>
    public List<FillMapping> Mappings { get; set; } = [];
}

/// <summary>
/// 批量执行填充请求
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class BatchExecuteFillRequest
{
    /// <summary>
    /// 客户端生成的执行幂等键；相同用户、文件和键的重试返回既有任务。
    /// </summary>
    public string? ExecutionRequestId { get; set; }

    /// <summary>
    /// 文件ID
    /// </summary>
    public int FileId { get; set; }

    /// <summary>
    /// 目标客户ID（限定匹配范围）
    /// </summary>
    public int? CustomerId { get; set; }

    /// <summary>
    /// 目标制程ID（限定匹配范围）
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// 目标机型ID（限定匹配范围）
    /// </summary>
    public int? MachineModelId { get; set; }

    /// <summary>
    /// 执行记录归档用的预览快照
    /// </summary>
    public List<ExecutionHistoryPreviewTableSnapshot> PreviewTables { get; set; } = [];

    /// <summary>
    /// 各表格的填充映射
    /// </summary>
    public List<BatchTableFillMapping> Tables { get; set; } = [];

    /// <summary>
    /// 匹配配置（用于执行前重算门禁）
    /// </summary>
    public MatchConfigDto? Config { get; set; }
}

/// <summary>
/// 智能填充编辑值回填验收规格请求。
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class SmartFillSpecBackfillRequest
{
    /// <summary>
    /// 当前匹配范围客户ID。
    /// </summary>
    public int? CustomerId { get; set; }

    /// <summary>
    /// 当前匹配范围制程ID。
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// 当前匹配范围机型ID。
    /// </summary>
    public int? MachineModelId { get; set; }

    /// <summary>
    /// 待回填行。
    /// </summary>
    public List<SmartFillSpecBackfillItem> Items { get; set; } = [];
}

/// <summary>
/// 智能填充编辑值回填行。
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class SmartFillSpecBackfillItem
{
    /// <summary>
    /// 已匹配规格ID；为空时新增规格。
    /// </summary>
    public int? SpecId { get; set; }

    /// <summary>
    /// 源文档项目。
    /// </summary>
    public string? SourceProject { get; set; }

    /// <summary>
    /// 源文档规格。
    /// </summary>
    public string? SourceSpecification { get; set; }

    /// <summary>
    /// 用户编辑后的验收标准。
    /// </summary>
    public string? OverrideAcceptance { get; set; }

    /// <summary>
    /// 用户编辑后的备注。
    /// </summary>
    public string? OverrideRemark { get; set; }
}

/// <summary>
/// 智能填充编辑值回填结果。
/// </summary>
public class SmartFillSpecBackfillResponse
{
    public int UpdatedCount { get; set; }

    public int CreatedCount { get; set; }

    public int TotalCount => UpdatedCount + CreatedCount;
}

/// <summary>
/// 批量回复来源上传响应
/// </summary>
public class BatchReplySourceUploadResponse
{
    /// <summary>
    /// 会话ID
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 来源文件名
    /// </summary>
    public string SourceFileName { get; set; } = string.Empty;

    /// <summary>
    /// 来源文件类型
    /// </summary>
    public UploadedFileType SourceFileType { get; set; }

    /// <summary>
    /// 表格数量
    /// </summary>
    public int TableCount { get; set; }
}

/// <summary>
/// 批量回复目标文件上传响应
/// </summary>
public class BatchReplyTargetUploadResponse
{
    /// <summary>
    /// 会话ID
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 本次上传成功的目标文件
    /// </summary>
    public List<BatchReplyUploadedTargetFileDto> Files { get; set; } = [];
}

/// <summary>
/// 批量回复已上传目标文件摘要
/// </summary>
public class BatchReplyUploadedTargetFileDto
{
    /// <summary>
    /// 目标文件临时标识
    /// </summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件类型
    /// </summary>
    public UploadedFileType FileType { get; set; }

    /// <summary>
    /// 表格数量
    /// </summary>
    public int TableCount { get; set; }
}

/// <summary>
/// 批量回复预检响应
/// </summary>
public class BatchReplyPreviewResponse
{
    /// <summary>
    /// 会话ID
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 来源文件名
    /// </summary>
    public string SourceFileName { get; set; } = string.Empty;

    /// <summary>
    /// 来源文件类型
    /// </summary>
    public UploadedFileType SourceFileType { get; set; }

    /// <summary>
    /// 是否严格模式
    /// </summary>
    public bool IsStrictMode { get; set; } = true;

    /// <summary>
    /// 是否使用 AI
    /// </summary>
    public bool UsesAi { get; set; } = false;

    /// <summary>
    /// 可直接应用数量
    /// </summary>
    public int ReadyCount => Files.Count(file => file.CanApply);

    /// <summary>
    /// 总文件数
    /// </summary>
    public int TotalCount => Files.Count;

    /// <summary>
    /// 逐文件预检结果
    /// </summary>
    public List<BatchReplyPreviewFileResult> Files { get; set; } = [];
}

/// <summary>
/// 批量回复逐文件预检结果
/// </summary>
public class BatchReplyPreviewFileResult
{
    /// <summary>
    /// 目标文件临时标识
    /// </summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 是否可应用
    /// </summary>
    public bool CanApply { get; set; }

    /// <summary>
    /// 失败原因列表
    /// </summary>
    public List<string> Errors { get; set; } = [];
}

/// <summary>
/// 批量回复单表预览请求
/// </summary>
public class BatchReplyTablePreviewRequest
{
    /// <summary>
    /// 会话ID
    /// </summary>
    [Required(ErrorMessage = "会话ID不能为空")]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 来源表配置
    /// </summary>
    public List<BatchTableConfig> SourceTables { get; set; } = [];

    /// <summary>
    /// 目标文件临时标识
    /// </summary>
    [Required(ErrorMessage = "目标文件不能为空")]
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// 当前预览的目标表配置
    /// </summary>
    [Required(ErrorMessage = "目标表配置不能为空")]
    public BatchTableConfig? TargetTable { get; set; }
}

/// <summary>
/// 批量回复重复键处理决议
/// </summary>
public class BatchReplyDuplicateResolutionDto
{
    /// <summary>
    /// 冲突组标识
    /// </summary>
    [Required(ErrorMessage = "冲突组标识不能为空")]
    public string GroupId { get; set; } = string.Empty;

    /// <summary>
    /// 处理策略（keepFirst / keepLast / skip）
    /// </summary>
    [Required(ErrorMessage = "处理策略不能为空")]
    public string Strategy { get; set; } = string.Empty;
}

/// <summary>
/// 批量回复重复键冲突组
/// </summary>
public class BatchReplyDuplicateGroupDto
{
    /// <summary>
    /// 冲突组标识
    /// </summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>
    /// 冲突来源（source / target）
    /// </summary>
    public string DuplicateSource { get; set; } = string.Empty;

    /// <summary>
    /// 所属表索引
    /// </summary>
    public int TableIndex { get; set; }

    /// <summary>
    /// 项目
    /// </summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// 规格
    /// </summary>
    public string Specification { get; set; } = string.Empty;

    /// <summary>
    /// 涉及行
    /// </summary>
    public List<BatchReplyDuplicateRowDto> Rows { get; set; } = [];
}

/// <summary>
/// 批量回复重复键冲突明细行
/// </summary>
public class BatchReplyDuplicateRowDto
{
    /// <summary>
    /// 行号
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// 项目
    /// </summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// 规格
    /// </summary>
    public string Specification { get; set; } = string.Empty;

    /// <summary>
    /// 验收值
    /// </summary>
    public string? Acceptance { get; set; }

    /// <summary>
    /// 备注值
    /// </summary>
    public string? Remark { get; set; }
}

/// <summary>
/// 批量回复单表预览响应
/// </summary>
public class BatchReplyTablePreviewResponse
{
    /// <summary>
    /// 目标文件临时标识
    /// </summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 目标表索引
    /// </summary>
    public int TableIndex { get; set; }

    /// <summary>
    /// 来源表索引
    /// </summary>
    public int SourceTableIndex { get; set; }

    /// <summary>
    /// 是否可应用
    /// </summary>
    public bool CanApply { get; set; }

    /// <summary>
    /// 错误列表
    /// </summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>
    /// 重复键冲突组
    /// </summary>
    public List<BatchReplyDuplicateGroupDto> DuplicateGroups { get; set; } = [];

    /// <summary>
    /// 逐行预览结果
    /// </summary>
    public List<BatchReplyTablePreviewRowDto> Rows { get; set; } = [];
}

/// <summary>
/// 批量回复单表逐行预览
/// </summary>
public class BatchReplyTablePreviewRowDto
{
    /// <summary>
    /// 目标行号
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// 项目
    /// </summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// 规格
    /// </summary>
    public string Specification { get; set; } = string.Empty;

    /// <summary>
    /// 预览写回的验收值
    /// </summary>
    public string Acceptance { get; set; } = string.Empty;

    /// <summary>
    /// 预览写回的备注值
    /// </summary>
    public string? Remark { get; set; }
}

/// <summary>
/// 批量回复执行请求
/// </summary>
public class BatchReplyExecuteRequest
{
    /// <summary>
    /// 会话ID
    /// </summary>
    [Required(ErrorMessage = "会话ID不能为空")]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 来源表配置（新流程使用）
    /// </summary>
    public List<BatchTableConfig> SourceTables { get; set; } = [];

    /// <summary>
    /// 目标文件执行配置（新流程使用）
    /// </summary>
    public List<BatchReplyExecuteTargetRequest> Targets { get; set; } = [];
}

/// <summary>
/// 批量回复单个目标文件执行配置
/// </summary>
public class BatchReplyExecuteTargetRequest
{
    /// <summary>
    /// 目标文件临时标识
    /// </summary>
    [Required(ErrorMessage = "目标文件不能为空")]
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// 当前目标文件参与执行的目标表配置
    /// </summary>
    public List<BatchTableConfig> Tables { get; set; } = [];
}

/// <summary>
/// 批量回复执行响应
/// </summary>
public class BatchReplyExecuteResponse
{
    /// <summary>
    /// 执行任务ID
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// 成功数量
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// 失败数量
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// 下载地址
    /// </summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// 下载文件名
    /// </summary>
    public string DownloadFileName { get; set; } = string.Empty;

    /// <summary>
    /// 逐文件执行结果
    /// </summary>
    public List<BatchReplyExecuteFileResult> Files { get; set; } = [];
}

/// <summary>
/// 批量回复逐文件执行结果
/// </summary>
public class BatchReplyExecuteFileResult
{
    /// <summary>
    /// 目标文件临时标识
    /// </summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 结果说明
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
