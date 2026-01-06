using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Core.Interfaces;

/// <summary>
/// 多层缓存服务接口 (P0-2)
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// 获取或创建向量缓存
    /// </summary>
    Task<float[]?> GetOrCreateEmbeddingAsync(string text, Func<Task<float[]>> factory);

    /// <summary>
    /// 获取或创建LLM统一分析结果缓存
    /// </summary>
    Task<UnifiedAnalysisResult?> GetOrCreateUnifiedAnalysisAsync(string query, string candidate, Func<Task<UnifiedAnalysisResult>> factory);

    /// <summary>
    /// 获取或创建完整匹配结果缓存
    /// </summary>
    Task<MatchResult?> GetOrCreateMatchResultAsync(MatchQuery query, Func<Task<MatchResult>> factory);

    /// <summary>
    /// 清除所有缓存
    /// </summary>
    Task ClearAllAsync();

    /// <summary>
    /// 获取缓存统计信息
    /// </summary>
    CacheStatistics GetStatistics();
}

/// <summary>
/// 缓存统计信息
/// </summary>
public class CacheStatistics
{
    public int VectorCacheCount { get; set; }
    public int LLMCacheCount { get; set; }
    public int ResultCacheCount { get; set; }
    public long VectorCacheHits { get; set; }
    public long VectorCacheMisses { get; set; }
    public long LLMCacheHits { get; set; }
    public long LLMCacheMisses { get; set; }
    public long ResultCacheHits { get; set; }
    public long ResultCacheMisses { get; set; }

    public float VectorHitRate => VectorCacheHits + VectorCacheMisses > 0
        ? (float)VectorCacheHits / (VectorCacheHits + VectorCacheMisses)
        : 0;

    public float LLMHitRate => LLMCacheHits + LLMCacheMisses > 0
        ? (float)LLMCacheHits / (LLMCacheHits + LLMCacheMisses)
        : 0;

    public float ResultHitRate => ResultCacheHits + ResultCacheMisses > 0
        ? (float)ResultCacheHits / (ResultCacheHits + ResultCacheMisses)
        : 0;
}
