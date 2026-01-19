using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 关键字Repository接口
/// </summary>
public interface IKeywordRepository : IRepository<Keyword>
{
    /// <summary>
    /// 根据词语获取关键字
    /// </summary>
    /// <param name="word">词语</param>
    /// <returns>关键字或null</returns>
    Task<Keyword?> GetByWordAsync(string word);

    /// <summary>
    /// 批量添加关键字（自动去重）
    /// </summary>
    /// <param name="words">词语列表</param>
    /// <returns>新添加的关键字数量</returns>
    Task<int> AddRangeUniqueAsync(IEnumerable<string> words);

    /// <summary>
    /// 获取所有关键字词语
    /// </summary>
    /// <returns>关键字词语列表</returns>
    Task<IReadOnlyList<string>> GetAllWordsAsync();

    /// <summary>
    /// 检查词语是否为关键字
    /// </summary>
    /// <param name="word">词语</param>
    /// <returns>是否为关键字</returns>
    Task<bool> IsKeywordAsync(string word);
}
