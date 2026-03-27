using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Core.TextProcessing.Models;

namespace AcceptanceSpecSystem.Core.TextProcessing.Services;

public class SynonymService : ISynonymService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly ISynonymDataProvider _synonymDataProvider;
    private readonly SemaphoreSlim _cacheRefreshLock = new(1, 1);

    // 实例级缓存，避免跨测试/跨作用域污染
    private IReadOnlyDictionary<string, string>? _cached;
    private DateTime _cachedAt;

    public SynonymService(ISynonymDataProvider synonymDataProvider)
    {
        _synonymDataProvider = synonymDataProvider;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetWordToStandardMapAsync(CancellationToken cancellationToken = default)
    {
        if (TryGetCachedMap(out var cached))
        {
            return cached;
        }

        await _cacheRefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCachedMap(out cached))
            {
                return cached;
            }

            var groups = await _synonymDataProvider.GetAllGroupsAsync(cancellationToken);
            var map = BuildWordToStandardMap(groups);
            _cached = map;
            _cachedAt = DateTime.UtcNow;
            return map;
        }
        finally
        {
            _cacheRefreshLock.Release();
        }
    }

    private bool TryGetCachedMap(out IReadOnlyDictionary<string, string> cached)
    {
        cached = EmptyCache;
        if (_cached == null)
        {
            return false;
        }

        if (DateTime.UtcNow - _cachedAt >= CacheDuration)
        {
            return false;
        }

        cached = _cached;
        return true;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyCache =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> BuildWordToStandardMap(IReadOnlyList<SynonymGroupModel> groups)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var standard = group.Words.FirstOrDefault(word => word.IsStandard)?.Word
                           ?? group.Words.FirstOrDefault()?.Word;

            if (string.IsNullOrWhiteSpace(standard))
            {
                continue;
            }

            foreach (var word in group.Words)
            {
                if (string.IsNullOrWhiteSpace(word.Word))
                {
                    continue;
                }

                map[word.Word] = standard;
            }

            map[standard] = standard;
        }

        return map;
    }
}
