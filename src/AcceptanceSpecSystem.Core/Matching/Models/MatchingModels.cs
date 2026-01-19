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
    /// 是否使用Embedding匹配
    /// </summary>
    public bool UseEmbedding { get; set; } = false;

    /// <summary>
    /// Embedding权重（使用时会重新归一化其他权重）
    /// </summary>
    public double EmbeddingWeight { get; set; } = 0.5;

    /// <summary>
    /// 最小匹配阈值
    /// </summary>
    public double MinScoreThreshold { get; set; } = 0.3;

    /// <summary>
    /// 返回的最大匹配数量
    /// </summary>
    public int MaxResults { get; set; } = 5;

    /// <summary>
    /// 获取归一化后的权重
    /// </summary>
    public Dictionary<string, double> GetNormalizedWeights()
    {
        var weights = new Dictionary<string, double>();
        double total = 0;

        if (UseLevenshtein)
        {
            weights["Levenshtein"] = LevenshteinWeight;
            total += LevenshteinWeight;
        }

        if (UseJaccard)
        {
            weights["Jaccard"] = JaccardWeight;
            total += JaccardWeight;
        }

        if (UseCosine)
        {
            weights["Cosine"] = CosineWeight;
            total += CosineWeight;
        }

        if (UseEmbedding)
        {
            weights["Embedding"] = EmbeddingWeight;
            total += EmbeddingWeight;
        }

        // 归一化
        if (total > 0)
        {
            foreach (var key in weights.Keys.ToList())
            {
                weights[key] /= total;
            }
        }

        return weights;
    }
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
