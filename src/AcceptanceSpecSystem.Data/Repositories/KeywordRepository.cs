using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 关键字Repository实现
/// </summary>
public class KeywordRepository : Repository<Keyword>, IKeywordRepository
{
    /// <summary>
    /// 创建KeywordRepository实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public KeywordRepository(AppDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 根据词语获取关键字记录。
    /// </summary>
    /// <param name="word">词语</param>
    /// <returns>关键字记录或 null</returns>
    public async Task<Keyword?> GetByWordAsync(string word)
    {
        return await _dbSet
            .FirstOrDefaultAsync(k => k.Word == word);
    }

    /// <summary>
    /// 批量新增关键字（自动去重，仅新增不存在的词语）。
    /// 注意：该方法只添加到 DbContext 跟踪，不主动调用 SaveChanges。
    /// </summary>
    /// <param name="words">词语集合</param>
    /// <returns>新增数量</returns>
    public async Task<int> AddRangeUniqueAsync(IEnumerable<string> words)
    {
        var existingWords = await _dbSet
            .Select(k => k.Word)
            .ToListAsync();

        var newWords = words
            .Distinct()
            .Where(w => !existingWords.Contains(w))
            .Select(w => new Keyword { Word = w, CreatedAt = DateTime.Now })
            .ToList();

        if (newWords.Count > 0)
        {
            await _dbSet.AddRangeAsync(newWords);
        }

        return newWords.Count;
    }

    /// <summary>
    /// 获取所有关键字词语列表。
    /// </summary>
    /// <returns>词语列表</returns>
    public async Task<IReadOnlyList<string>> GetAllWordsAsync()
    {
        return await _dbSet
            .Select(k => k.Word)
            .ToListAsync();
    }

    /// <summary>
    /// 判断指定词语是否为关键字。
    /// </summary>
    /// <param name="word">词语</param>
    /// <returns>是否为关键字</returns>
    public async Task<bool> IsKeywordAsync(string word)
    {
        return await _dbSet
            .AnyAsync(k => k.Word == word);
    }
}
