using System.ComponentModel.DataAnnotations;

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
    /// 是否使用Levenshtein距离
    /// </summary>
    public bool UseLevenshtein { get; set; } = true;

    /// <summary>
    /// Levenshtein权重
    /// </summary>
    public double LevenshteinWeight { get; set; } = 0.3;

    /// <summary>
    /// 是否使用Jaccard相似度
    /// </summary>
    public bool UseJaccard { get; set; } = true;

    /// <summary>
    /// Jaccard权重
    /// </summary>
    public double JaccardWeight { get; set; } = 0.3;

    /// <summary>
    /// 是否使用Cosine相似度
    /// </summary>
    public bool UseCosine { get; set; } = true;

    /// <summary>
    /// Cosine权重
    /// </summary>
    public double CosineWeight { get; set; } = 0.4;

    /// <summary>
    /// 最小匹配阈值
    /// </summary>
    public double MinScoreThreshold { get; set; } = 0.3;

    /// <summary>
    /// 返回的最大候选数量
    /// </summary>
    public int MaxCandidates { get; set; } = 5;
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
    /// 候选匹配列表（按得分降序）
    /// </summary>
    public List<MatchResultDto> Candidates { get; set; } = [];

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
    /// 综合得分（0-1）
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// 各算法得分详情
    /// </summary>
    public Dictionary<string, double> ScoreDetails { get; set; } = [];
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
