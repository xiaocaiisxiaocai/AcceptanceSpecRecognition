using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 同义词Repository实现
/// </summary>
public class SynonymRepository : ISynonymRepository
{
    private readonly AppDbContext _context;

    /// <summary>
    /// 创建SynonymRepository实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public SynonymRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 获取所有同义词组（包含组内词语）。
    /// </summary>
    /// <returns>同义词组列表</returns>
    public async Task<IReadOnlyList<SynonymGroup>> GetAllGroupsAsync()
    {
        return await _context.SynonymGroups
            .Include(g => g.Words)
            .ToListAsync();
    }

    /// <summary>
    /// 根据组ID获取同义词组（包含组内词语）。
    /// </summary>
    /// <param name="id">同义词组ID</param>
    /// <returns>同义词组或 null</returns>
    public async Task<SynonymGroup?> GetGroupByIdAsync(int id)
    {
        return await _context.SynonymGroups
            .Include(g => g.Words)
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    /// <summary>
    /// 根据任意词语获取所属同义词组（包含组内词语）。
    /// </summary>
    /// <param name="word">词语</param>
    /// <returns>同义词组或 null</returns>
    public async Task<SynonymGroup?> GetGroupByWordAsync(string word)
    {
        var synonymWord = await _context.SynonymWords
            .Include(w => w.Group)
            .ThenInclude(g => g.Words)
            .FirstOrDefaultAsync(w => w.Word == word);

        return synonymWord?.Group;
    }

    /// <summary>
    /// 新增同义词组。
    /// 约定：传入的第一个词语会被标记为标准词（IsStandard=true）。
    /// 注意：该方法只添加到 DbContext 跟踪，不主动调用 SaveChanges。
    /// </summary>
    /// <param name="words">词语集合</param>
    /// <returns>创建的同义词组</returns>
    public async Task<SynonymGroup> AddGroupAsync(IEnumerable<string> words)
    {
        var wordList = words.ToList();
        var group = new SynonymGroup
        {
            CreatedAt = DateTime.Now
        };

        for (var i = 0; i < wordList.Count; i++)
        {
            group.Words.Add(new SynonymWord
            {
                Word = wordList[i],
                IsStandard = i == 0
            });
        }

        await _context.SynonymGroups.AddAsync(group);
        return group;
    }

    /// <summary>
    /// 更新同义词组的词语集合（会清空旧词语后重新写入）。
    /// 约定：传入的第一个词语会被标记为标准词（IsStandard=true）。
    /// 注意：该方法只修改 DbContext 跟踪实体，不主动调用 SaveChanges。
    /// </summary>
    /// <param name="groupId">同义词组ID</param>
    /// <param name="words">新的词语集合</param>
    public async Task UpdateGroupAsync(int groupId, IEnumerable<string> words)
    {
        var group = await _context.SynonymGroups
            .Include(g => g.Words)
            .FirstOrDefaultAsync(g => g.Id == groupId);

        if (group == null) return;

        // 删除旧词语
        _context.SynonymWords.RemoveRange(group.Words);

        // 添加新词语
        var wordList = words.ToList();
        for (var i = 0; i < wordList.Count; i++)
        {
            group.Words.Add(new SynonymWord
            {
                Word = wordList[i],
                IsStandard = i == 0,
                GroupId = groupId
            });
        }

        group.UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 删除同义词组。
    /// 注意：该方法只修改 DbContext 跟踪实体，不主动调用 SaveChanges。
    /// </summary>
    /// <param name="groupId">同义词组ID</param>
    public async Task DeleteGroupAsync(int groupId)
    {
        var group = await _context.SynonymGroups.FindAsync(groupId);
        if (group != null)
        {
            _context.SynonymGroups.Remove(group);
        }
    }

    /// <summary>
    /// 获取指定词语的同义词列表；若不存在同义词组则返回仅包含自身的列表。
    /// </summary>
    /// <param name="word">词语</param>
    /// <returns>同义词列表</returns>
    public async Task<IReadOnlyList<string>> GetSynonymsAsync(string word)
    {
        var group = await GetGroupByWordAsync(word);
        return group?.Words.Select(w => w.Word).ToList() ?? new List<string> { word };
    }

    /// <summary>
    /// 获取指定词语的标准词；若不存在同义词组则返回自身。
    /// </summary>
    /// <param name="word">词语</param>
    /// <returns>标准词</returns>
    public async Task<string> GetStandardWordAsync(string word)
    {
        var group = await GetGroupByWordAsync(word);
        return group?.Words.FirstOrDefault(w => w.IsStandard)?.Word ?? word;
    }
}
