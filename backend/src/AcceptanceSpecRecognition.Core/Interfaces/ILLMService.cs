using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Core.Interfaces;

/// <summary>
/// 大模型服务接口
/// </summary>
public interface ILLMService
{
    /// <summary>
    /// 统一分析方法 - 一次调用完成冲突检测、语义等价分析、置信度评估
    /// </summary>
    /// <param name="query">查询文本</param>
    /// <param name="candidate">候选文本</param>
    /// <returns>统一分析结果</returns>
    Task<UnifiedAnalysisResult> AnalyzeUnifiedAsync(string query, string candidate);

    /// <summary>
    /// 流式分析候选结果，输出详细思考过程
    /// </summary>
    IAsyncEnumerable<StreamingAnalysisEvent> AnalyzeMatchesStreamingAsync(
        MatchQuery query,
        List<MatchCandidate> candidates,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取模型信息
    /// </summary>
    LLMModelInfo GetModelInfo();
}

/// <summary>
/// 统一分析结果（合并冲突检测 + 语义等价分析）
/// </summary>
public class UnifiedAnalysisResult
{
    /// <summary>
    /// 是否存在互斥冲突
    /// </summary>
    public bool HasConflict { get; set; }

    /// <summary>
    /// 冲突类型（none/electrical/protocol/mechanical）
    /// </summary>
    public string ConflictType { get; set; } = "none";

    /// <summary>
    /// 冲突描述
    /// </summary>
    public string ConflictDescription { get; set; } = string.Empty;

    /// <summary>
    /// 是否语义等价（品牌名、术语等）
    /// </summary>
    public bool IsEquivalent { get; set; }

    /// <summary>
    /// 等价映射列表
    /// </summary>
    public List<EquivalenceMapping> EquivalenceMappings { get; set; } = new();

    /// <summary>
    /// 分数调整系数（1.0=不变，>1.0=提升，<1.0=降低）
    /// </summary>
    public float ScoreAdjustmentFactor { get; set; } = 1.0f;

    /// <summary>
    /// 匹配置信度 (0-1)
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// 推理说明
    /// </summary>
    public string Reasoning { get; set; } = string.Empty;
}

/// <summary>
/// 等价映射关系
/// </summary>
public class EquivalenceMapping
{
    /// <summary>
    /// 查询中的术语
    /// </summary>
    public string QueryTerm { get; set; } = string.Empty;

    /// <summary>
    /// 候选中的术语
    /// </summary>
    public string CandidateTerm { get; set; } = string.Empty;

    /// <summary>
    /// 等价类型（brand=品牌名, synonym=同义词, traditional_simplified=繁简体等）
    /// </summary>
    public string Type { get; set; } = string.Empty;
}

/// <summary>
/// LLM模型信息
/// </summary>
public class LLMModelInfo
{
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
}
