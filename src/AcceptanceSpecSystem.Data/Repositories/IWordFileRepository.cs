using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// Word文件Repository接口
/// </summary>
public interface IWordFileRepository : IRepository<WordFile>
{
    /// <summary>
    /// 根据文件哈希值获取文件
    /// </summary>
    /// <param name="fileHash">文件哈希值</param>
    /// <returns>Word文件或null</returns>
    Task<WordFile?> GetByHashAsync(string fileHash);

    /// <summary>
    /// 检查文件哈希是否存在
    /// </summary>
    /// <param name="fileHash">文件哈希值</param>
    /// <returns>是否存在</returns>
    Task<bool> ExistsByHashAsync(string fileHash);

    /// <summary>
    /// 获取所有文件（不含内容）
    /// </summary>
    /// <returns>Word文件列表（不含FileContent）</returns>
    Task<IReadOnlyList<WordFile>> GetAllWithoutContentAsync();
}
