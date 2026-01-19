using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// Word文件Repository实现
/// </summary>
public class WordFileRepository : Repository<WordFile>, IWordFileRepository
{
    /// <summary>
    /// 创建WordFileRepository实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public WordFileRepository(AppDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 根据文件哈希获取 Word 文件记录。
    /// </summary>
    /// <param name="fileHash">文件哈希</param>
    /// <returns>Word 文件记录或 null</returns>
    public async Task<WordFile?> GetByHashAsync(string fileHash)
    {
        return await _dbSet
            .FirstOrDefaultAsync(f => f.FileHash == fileHash);
    }

    /// <summary>
    /// 判断是否存在指定哈希的 Word 文件记录。
    /// </summary>
    /// <param name="fileHash">文件哈希</param>
    /// <returns>是否存在</returns>
    public async Task<bool> ExistsByHashAsync(string fileHash)
    {
        return await _dbSet
            .AnyAsync(f => f.FileHash == fileHash);
    }

    /// <summary>
    /// 获取所有 Word 文件记录（不包含文件二进制内容），用于列表展示减少数据量。
    /// </summary>
    /// <returns>Word 文件列表（FileContent 为空）</returns>
    public async Task<IReadOnlyList<WordFile>> GetAllWithoutContentAsync()
    {
        return await _dbSet
            .Select(f => new WordFile
            {
                Id = f.Id,
                FileName = f.FileName,
                FileHash = f.FileHash,
                UploadedAt = f.UploadedAt
            })
            .ToListAsync();
    }
}
