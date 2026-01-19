using AcceptanceSpecSystem.Core.AI.Interfaces;
using AcceptanceSpecSystem.Data.Repositories;
using AcceptanceSpecSystem.Core.Matching.Interfaces;

namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// Embedding 服务：从 DB 的默认 AiServiceConfig 读取配置并生成 embedding
/// </summary>
public class ConfigurableEmbeddingService : IEmbeddingService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAiEmbeddingConnectorFactory _factory;

    private static readonly object CacheLock = new();
    private static int? _cachedConfigId;
    private static DateTime? _cachedConfigUpdatedAt;
    private static IAiEmbeddingConnector? _cachedConnector;
    private static DateTime _cachedAt;

    public ConfigurableEmbeddingService(IUnitOfWork unitOfWork, IAiEmbeddingConnectorFactory factory)
    {
        _unitOfWork = unitOfWork;
        _factory = factory;
    }

    public bool IsAvailable
    {
        get
        {
            // 为了不在 getter 里做 IO，这里只做“是否可能可用”的粗判断
            // 实际调用时若无配置会抛异常，并在 HybridMatchingService 内降级。
            return true;
        }
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var connector = await GetConnectorAsync();
        return await connector.GenerateEmbeddingAsync(text);
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts)
    {
        var connector = await GetConnectorAsync();
        var list = texts.ToList();
        var results = new List<float[]>(list.Count);
        foreach (var t in list)
        {
            results.Add(await connector.GenerateEmbeddingAsync(t));
        }
        return results;
    }

    public double ComputeSimilarity(float[] embedding1, float[] embedding2)
    {
        if (embedding1.Length == 0 || embedding2.Length == 0) return 0;
        if (embedding1.Length != embedding2.Length) return 0;

        double dot = 0, norm1 = 0, norm2 = 0;
        for (var i = 0; i < embedding1.Length; i++)
        {
            dot += embedding1[i] * embedding2[i];
            norm1 += embedding1[i] * embedding1[i];
            norm2 += embedding2[i] * embedding2[i];
        }

        if (norm1 == 0 || norm2 == 0) return 0;
        var cos = dot / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
        return Math.Clamp(cos, 0, 1);
    }

    private async Task<IAiEmbeddingConnector> GetConnectorAsync()
    {
        lock (CacheLock)
        {
            if (_cachedConnector != null && (DateTime.Now - _cachedAt).TotalSeconds < 15)
            {
                return _cachedConnector;
            }
        }

        var cfg = await _unitOfWork.AiServiceConfigs.GetDefaultAsync();
        if (cfg == null)
            throw new InvalidOperationException("未配置默认 AI 服务（AiServiceConfig.IsDefault）");

        lock (CacheLock)
        {
            if (_cachedConnector != null &&
                _cachedConfigId == cfg.Id &&
                _cachedConfigUpdatedAt == cfg.UpdatedAt)
            {
                _cachedAt = DateTime.Now;
                return _cachedConnector;
            }
        }

        var connector = _factory.Create(cfg);

        lock (CacheLock)
        {
            _cachedConfigId = cfg.Id;
            _cachedConfigUpdatedAt = cfg.UpdatedAt;
            _cachedConnector = connector;
            _cachedAt = DateTime.Now;
            return _cachedConnector;
        }
    }
}

