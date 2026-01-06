using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Core.Services;

/// <summary>
/// 多层缓存服务实现 (P0-2)
/// </summary>
public class CacheService : ICacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly IConfigManager _configManager;

    // 缓存统计
    private long _vectorHits;
    private long _vectorMisses;
    private long _llmHits;
    private long _llmMisses;
    private long _resultHits;
    private long _resultMisses;

    public CacheService(IMemoryCache memoryCache, IConfigManager configManager)
    {
        _memoryCache = memoryCache;
        _configManager = configManager;
    }

    public async Task<float[]?> GetOrCreateEmbeddingAsync(string text, Func<Task<float[]>> factory)
    {
        var config = _configManager.GetAll();
        if (!config.Cache.EnableVectorCache)
        {
            Interlocked.Increment(ref _vectorMisses);
            return await factory();
        }

        var key = $"emb:{ComputeHash(text)}";

        if (_memoryCache.TryGetValue<float[]>(key, out var cached))
        {
            Interlocked.Increment(ref _vectorHits);
            return cached;
        }

        Interlocked.Increment(ref _vectorMisses);
        var result = await factory();

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(config.Cache.VectorCacheTtlMinutes),
            Size = 1
        };

        _memoryCache.Set(key, result, options);
        return result;
    }

    public async Task<UnifiedAnalysisResult?> GetOrCreateUnifiedAnalysisAsync(
        string query,
        string candidate,
        Func<Task<UnifiedAnalysisResult>> factory)
    {
        var config = _configManager.GetAll();
        if (!config.Cache.EnableLLMCache)
        {
            Interlocked.Increment(ref _llmMisses);
            return await factory();
        }

        var key = $"unified:{ComputeHash(query + "|" + candidate)}";

        if (_memoryCache.TryGetValue<UnifiedAnalysisResult>(key, out var cached))
        {
            Interlocked.Increment(ref _llmHits);
            return cached;
        }

        Interlocked.Increment(ref _llmMisses);
        var result = await factory();

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(config.Cache.LLMCacheTtlMinutes),
            Size = 1
        };

        _memoryCache.Set(key, result, options);
        return result;
    }

    public async Task<MatchResult?> GetOrCreateMatchResultAsync(
        MatchQuery query,
        Func<Task<MatchResult>> factory)
    {
        var config = _configManager.GetAll();
        if (!config.Cache.EnableResultCache)
        {
            Interlocked.Increment(ref _resultMisses);
            return await factory();
        }

        var fingerprint = ComputeQueryFingerprint(query);
        var configVersion = config.Version;
        var key = $"match:{fingerprint}:{configVersion}";

        if (_memoryCache.TryGetValue<MatchResult>(key, out var cached))
        {
            Interlocked.Increment(ref _resultHits);
            return cached;
        }

        Interlocked.Increment(ref _resultMisses);
        var result = await factory();

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(config.Cache.ResultCacheTtlMinutes),
            Size = 10 // 完整结果占用更多空间
        };

        _memoryCache.Set(key, result, options);
        return result;
    }

    public Task ClearAllAsync()
    {
        if (_memoryCache is MemoryCache cache)
        {
            // .NET 8+ 使用 Clear() 方法彻底清空缓存
            cache.Clear();
        }

        Interlocked.Exchange(ref _vectorHits, 0);
        Interlocked.Exchange(ref _vectorMisses, 0);
        Interlocked.Exchange(ref _llmHits, 0);
        Interlocked.Exchange(ref _llmMisses, 0);
        Interlocked.Exchange(ref _resultHits, 0);
        Interlocked.Exchange(ref _resultMisses, 0);

        return Task.CompletedTask;
    }

    public CacheStatistics GetStatistics()
    {
        // 获取当前缓存条目数（近似值）
        var vectorCount = 0;
        var llmCount = 0;
        var resultCount = 0;

        // Note: MemoryCache 不提供精确的条目计数，这里返回估算值
        // 实际生产环境可以使用自定义计数器跟踪

        return new CacheStatistics
        {
            VectorCacheCount = vectorCount,
            LLMCacheCount = llmCount,
            ResultCacheCount = resultCount,
            VectorCacheHits = Interlocked.Read(ref _vectorHits),
            VectorCacheMisses = Interlocked.Read(ref _vectorMisses),
            LLMCacheHits = Interlocked.Read(ref _llmHits),
            LLMCacheMisses = Interlocked.Read(ref _llmMisses),
            ResultCacheHits = Interlocked.Read(ref _resultHits),
            ResultCacheMisses = Interlocked.Read(ref _resultMisses)
        };
    }

    /// <summary>
    /// 计算文本哈希
    /// </summary>
    private static string ComputeHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash).Replace("/", "_").Replace("+", "-");
    }

    /// <summary>
    /// 计算查询指纹
    /// </summary>
    private static string ComputeQueryFingerprint(MatchQuery query)
    {
        var json = JsonSerializer.Serialize(query);
        return ComputeHash(json);
    }
}
