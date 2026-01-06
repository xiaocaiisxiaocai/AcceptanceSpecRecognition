namespace AcceptanceSpecRecognition.Core.Models;

/// <summary>
/// 匹配查询
/// </summary>
public class MatchQuery
{
    public string Project { get; set; } = string.Empty;
    public string TechnicalSpec { get; set; } = string.Empty;
}

/// <summary>
/// 匹配结果
/// </summary>
public class MatchResult
{
    /// <summary>
    /// 原始查询
    /// </summary>
    public MatchQuery Query { get; set; } = new();

    /// <summary>
    /// 最佳匹配（只返回一个）
    /// </summary>
    public MatchCandidate? BestMatch { get; set; }

    /// <summary>
    /// 相似度分数
    /// </summary>
    public float SimilarityScore { get; set; }

    /// <summary>
    /// 置信度等级
    /// </summary>
    public ConfidenceLevel Confidence { get; set; }

    /// <summary>
    /// 是否为低置信度（需要用户注意）
    /// </summary>
    public bool IsLowConfidence => Confidence == ConfidenceLevel.Low;

    /// <summary>
    /// 匹配模式：Embedding / LLM+Embedding
    /// </summary>
    public string MatchMode { get; set; } = "Embedding";

    /// <summary>
    /// 匹配耗时（毫秒）
    /// </summary>
    public long DurationMs { get; set; }
}

/// <summary>
/// 匹配候选
/// </summary>
public class MatchCandidate
{
    public HistoryRecord Record { get; set; } = new();
    public float SimilarityScore { get; set; }
    public string HighlightedActualSpec { get; set; } = string.Empty;
    public string HighlightedRemark { get; set; } = string.Empty;
    public MatchExplanation? Explanation { get; set; }
}

/// <summary>
/// 匹配解释
/// </summary>
public class MatchExplanation
{
    public float EmbeddingSimilarity { get; set; }
    public float? LLMAdjustedScore { get; set; }
    public string? LLMReasoning { get; set; }
    public List<string> MatchedKeywords { get; set; } = new();
    public List<string> MatchedSynonyms { get; set; } = new();
    public List<PreprocessingStep> PreprocessingSteps { get; set; } = new();
}

/// <summary>
/// 预处理步骤
/// </summary>
public class PreprocessingStep
{
    public string Type { get; set; } = string.Empty;
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
}

/// <summary>
/// 置信度等级
/// </summary>
public enum ConfidenceLevel
{
    /// <summary>
    /// 匹配成功 - 相似度 >= 阈值
    /// </summary>
    Success,

    /// <summary>
    /// 低置信度 - 相似度 < 阈值，需要人工确认
    /// </summary>
    Low
}

/// <summary>
/// LLM分析结果
/// </summary>
public class LLMAnalysisResult
{
    public int BestMatchIndex { get; set; }
    public float Confidence { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public List<ConflictInfo> Conflicts { get; set; } = new();
    public List<float> AdjustedScores { get; set; } = new();
}

/// <summary>
/// 冲突信息
/// </summary>
public class ConflictInfo
{
    public string Type { get; set; } = string.Empty;
    public string QueryValue { get; set; } = string.Empty;
    public string CandidateValue { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
}

#region 流式分析模型

/// <summary>
/// 思考步骤类型
/// </summary>
public enum ThinkingStepType
{
    /// <summary>
    /// 属性提取 - 解析查询和候选的关键属性
    /// </summary>
    Extract,

    /// <summary>
    /// 语义对比 - 逐项对比属性匹配情况
    /// </summary>
    Compare,

    /// <summary>
    /// 冲突检测 - 检测互斥属性冲突
    /// </summary>
    Conflict,

    /// <summary>
    /// 置信度推理 - 计算并解释置信度
    /// </summary>
    Confidence,

    /// <summary>
    /// 最终结论 - 输出最佳匹配和推理说明
    /// </summary>
    Conclusion
}

/// <summary>
/// 思考步骤
/// </summary>
public class ThinkingStep
{
    /// <summary>
    /// 步骤类型：extract, compare, conflict, confidence, conclusion
    /// </summary>
    public string Step { get; set; } = string.Empty;

    /// <summary>
    /// 步骤标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 步骤内容（JSON 对象）
    /// </summary>
    public object Content { get; set; } = new { };

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 流式分析事件
/// </summary>
public class StreamingAnalysisEvent
{
    /// <summary>
    /// 事件类型：thinking, result, error, done
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// 思考步骤（EventType 为 thinking 时）
    /// </summary>
    public ThinkingStep? ThinkingStep { get; set; }

    /// <summary>
    /// 分析结果（EventType 为 result 时）
    /// </summary>
    public LLMAnalysisResult? Result { get; set; }

    /// <summary>
    /// 错误信息（EventType 为 error 时）
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// 预处理和初步匹配结果（用于流式输出的第一阶段）
/// </summary>
public class PreprocessMatchResult
{
    /// <summary>
    /// 预处理后的文本
    /// </summary>
    public string PreprocessedText { get; set; } = string.Empty;

    /// <summary>
    /// 候选列表（按相似度排序）
    /// </summary>
    public List<MatchCandidate> Candidates { get; set; } = new();

    /// <summary>
    /// 最佳匹配候选
    /// </summary>
    public MatchCandidate? BestMatch { get; set; }

    /// <summary>
    /// 最佳匹配分数
    /// </summary>
    public float BestScore { get; set; }
}

#endregion
