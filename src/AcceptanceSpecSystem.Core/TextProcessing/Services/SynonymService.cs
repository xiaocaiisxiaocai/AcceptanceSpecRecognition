using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Core.TextProcessing.Services;

public class SynonymService : ISynonymService
{
    private readonly IUnitOfWork _unitOfWork;

    // 简单内存缓存，避免每次匹配都全表读取
    private static IReadOnlyDictionary<string, string>? _cached;
    private static DateTime _cachedAt;
    private static readonly object CacheLock = new();

    public SynonymService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetWordToStandardMapAsync(CancellationToken cancellationToken = default)
    {
        lock (CacheLock)
        {
            if (_cached != null && (DateTime.Now - _cachedAt).TotalSeconds < 30)
                return _cached;
        }

        var groups = await _unitOfWork.Synonyms.GetAllGroupsAsync();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var g in groups)
        {
            var standard = g.Words.FirstOrDefault(w => w.IsStandard)?.Word
                           ?? g.Words.FirstOrDefault()?.Word;

            if (string.IsNullOrWhiteSpace(standard))
                continue;

            foreach (var w in g.Words)
            {
                if (string.IsNullOrWhiteSpace(w.Word))
                    continue;
                map[w.Word] = standard;
            }

            // 标准词映射到自身
            map[standard] = standard;
        }

        lock (CacheLock)
        {
            _cached = map;
            _cachedAt = DateTime.Now;
            return _cached;
        }
    }
}

