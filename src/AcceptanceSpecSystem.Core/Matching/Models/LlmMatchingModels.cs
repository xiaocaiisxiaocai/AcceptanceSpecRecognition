namespace AcceptanceSpecSystem.Core.Matching.Models;

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
    public int? LlmServiceId { get; set; }
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
/// LLM 生成建议请求
/// </summary>
public class LlmSuggestionRequest
{
    public string SourceProject { get; set; } = string.Empty;
    public string SourceSpecification { get; set; } = string.Empty;

    /// <summary>
    /// 参考数据：最佳匹配的项目名称（如果有）
    /// </summary>
    public string? BestMatchProject { get; set; }

    /// <summary>
    /// 参考数据：最佳匹配的规格内容
    /// </summary>
    public string? BestMatchSpecification { get; set; }

    /// <summary>
    /// 参考数据：最佳匹配的验收标准
    /// </summary>
    public string? BestMatchAcceptance { get; set; }

    /// <summary>
    /// 参考数据：最佳匹配的备注
    /// </summary>
    public string? BestMatchRemark { get; set; }

    /// <summary>
    /// 参考数据：最佳匹配得分（0-1）
    /// </summary>
    public double? BestMatchScore { get; set; }

    public int? LlmServiceId { get; set; }
}

/// <summary>
/// LLM 生成建议结果
/// </summary>
public class LlmSuggestionResult
{
    public string? Acceptance { get; set; }
    public string? Remark { get; set; }
    public string? Reason { get; set; }
}
