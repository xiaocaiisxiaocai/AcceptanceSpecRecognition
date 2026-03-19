using System.ComponentModel.DataAnnotations;
using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Api.DTOs;

/// <summary>
/// 匹配预览请求
/// </summary>
public class MatchPreviewRequest
{
    /// <summary>
    /// 文件ID（文件模式：从Word表格提取项目/规格作为待匹配文本）
    /// </summary>
    public int? FileId { get; set; }

    /// <summary>
    /// 表格索引（文件模式）
    /// </summary>
    public int? TableIndex { get; set; }

    /// <summary>
    /// 项目列索引（文件模式，必须由用户指定）
    /// </summary>
    public int? ProjectColumnIndex { get; set; }

    /// <summary>
    /// 规格列索引（文件模式，必须由用户指定）
    /// </summary>
    public int? SpecificationColumnIndex { get; set; }

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

    /// <summary>
    /// 待匹配的文本列表
    /// </summary>
    public List<MatchSourceItem> Items { get; set; } = [];

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
    /// 匹配配置
    /// </summary>
    public MatchConfigDto? Config { get; set; }
}

/// <summary>
/// 待匹配的源项
/// </summary>
public class MatchSourceItem
{
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
    /// 匹配策略
    /// </summary>
    public MatchingStrategy MatchingStrategy { get; set; } = MatchingStrategy.SingleStage;

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
    /// 是否启用LLM复核
    /// </summary>
    public bool UseLlmReview { get; set; } = false;

    /// <summary>
    /// 是否启用LLM生成建议
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
/// 匹配预览响应
/// </summary>
public class MatchPreviewResponse
{
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
/// 匹配预览项
/// </summary>
public class MatchPreviewItem
{
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
    /// LLM 生成建议（低置信度/无匹配时）
    /// </summary>
    public LlmSuggestionDto? LlmSuggestion { get; set; }

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
    public string ConfidenceLevel => BestMatch?.Score switch
    {
        >= 0.8 => "high",
        >= 0.6 => "medium",
        > 0 => "low",
        _ => "none"
    };
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
    /// 匹配策略
    /// </summary>
    public MatchingStrategy MatchingStrategy { get; set; }

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
    /// LLM复核得分（0-100）
    /// </summary>
    public double? LlmScore { get; set; }

    /// <summary>
    /// LLM复核原因
    /// </summary>
    public string? LlmReason { get; set; }

    /// <summary>
    /// LLM复核评论
    /// </summary>
    public string? LlmCommentary { get; set; }

    /// <summary>
    /// 是否经过LLM复核
    /// </summary>
    public bool IsLlmReviewed { get; set; }
}

/// <summary>
/// 执行填充请求
/// </summary>
public class ExecuteFillRequest
{
    /// <summary>
    /// 文件ID（前端兼容字段）
    /// </summary>
    public int? FileId { get; set; }

    /// <summary>
    /// 表格索引（前端兼容字段）
    /// </summary>
    public int? TableIndex { get; set; }

    /// <summary>
    /// 源文件ID（旧字段，兼容保留）
    /// </summary>
    public int? SourceFileId { get; set; }

    /// <summary>
    /// 源表格索引（旧字段，兼容保留）
    /// </summary>
    public int? SourceTableIndex { get; set; }

    /// <summary>
    /// 填充映射列表
    /// </summary>
    [Required(ErrorMessage = "填充映射不能为空")]
    public List<FillMapping> Mappings { get; set; } = [];

    /// <summary>
    /// 验收标准填充目标列索引
    /// </summary>
    public int? AcceptanceColumnIndex { get; set; }

    /// <summary>
    /// 备注填充目标列索引
    /// </summary>
    public int? RemarkColumnIndex { get; set; }
}

/// <summary>
/// 填充映射
/// </summary>
public class FillMapping
{
    /// <summary>
    /// 行索引
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// 选择的验收规格ID（前端兼容字段）
    /// </summary>
    public int? SpecId { get; set; }

    /// <summary>
    /// 选择的验收规格ID（旧字段，兼容保留）
    /// </summary>
    public int? SelectedSpecId { get; set; }

    /// <summary>
    /// 是否使用LLM生成建议
    /// </summary>
    public bool UseLlmSuggestion { get; set; }

    /// <summary>
    /// LLM生成的验收标准（可选）
    /// </summary>
    public string? Acceptance { get; set; }

    /// <summary>
    /// LLM生成的备注（可选）
    /// </summary>
    public string? Remark { get; set; }
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
/// 相似度计算请求
/// </summary>
public class SimilarityRequest
{
    /// <summary>
    /// 文本1
    /// </summary>
    [Required(ErrorMessage = "文本1不能为空")]
    public string Text1 { get; set; } = string.Empty;

    /// <summary>
    /// 文本2
    /// </summary>
    [Required(ErrorMessage = "文本2不能为空")]
    public string Text2 { get; set; } = string.Empty;

    /// <summary>
    /// 匹配配置
    /// </summary>
    public MatchConfigDto? Config { get; set; }
}

/// <summary>
/// 相似度计算响应
/// </summary>
public class SimilarityResponse
{
    /// <summary>
    /// 综合得分
    /// </summary>
    public double TotalScore { get; set; }

    /// <summary>
    /// 各算法得分详情
    /// </summary>
    public Dictionary<string, double> Scores { get; set; } = [];
}

/// <summary>
/// LLM 流式请求
/// </summary>
public class MatchLlmStreamRequest
{
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
}

/// <summary>
/// LLM生成建议DTO
/// </summary>
public class LlmSuggestionDto
{
    /// <summary>
    /// 验收标准建议
    /// </summary>
    public string? Acceptance { get; set; }

    /// <summary>
    /// 备注建议
    /// </summary>
    public string? Remark { get; set; }

    /// <summary>
    /// 生成理由
    /// </summary>
    public string? Reason { get; set; }
}

// ===== 批量填充 DTO =====

/// <summary>
/// 批量表格配置（单个表格的列索引）
/// </summary>
public class BatchTableConfig
{
    /// <summary>
    /// 表格索引
    /// </summary>
    public int TableIndex { get; set; }

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

    /// <summary>
    /// 是否过滤项目列与规格列都为空的源行（表格级，可选；未传时走全局配置）
    /// </summary>
    public bool? FilterEmptySourceRows { get; set; }
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
/// 批量填充映射（按表格分组）
/// </summary>
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
    /// 该表格的填充映射列表
    /// </summary>
    public List<FillMapping> Mappings { get; set; } = [];
}

/// <summary>
/// 批量执行填充请求
/// </summary>
public class BatchExecuteFillRequest
{
    /// <summary>
    /// 文件ID
    /// </summary>
    public int FileId { get; set; }

    /// <summary>
    /// 各表格的填充映射
    /// </summary>
    public List<BatchTableFillMapping> Tables { get; set; } = [];
}
