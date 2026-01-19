using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 同义词Repository接口
/// </summary>
public interface ISynonymRepository
{
    /// <summary>
    /// 获取所有同义词组
    /// </summary>
    /// <returns>同义词组列表（包含词语）</returns>
    Task<IReadOnlyList<SynonymGroup>> GetAllGroupsAsync();

    /// <summary>
    /// 根据ID获取同义词组
    /// </summary>
    /// <param name="id">同义词组ID</param>
    /// <returns>同义词组（包含词语）或null</returns>
    Task<SynonymGroup?> GetGroupByIdAsync(int id);

    /// <summary>
    /// 根据词语查找所属的同义词组
    /// </summary>
    /// <param name="word">词语</param>
    /// <returns>同义词组（包含词语）或null</returns>
    Task<SynonymGroup?> GetGroupByWordAsync(string word);

    /// <summary>
    /// 添加同义词组
    /// </summary>
    /// <param name="words">词语列表（第一个为标准词）</param>
    /// <returns>新创建的同义词组</returns>
    Task<SynonymGroup> AddGroupAsync(IEnumerable<string> words);

    /// <summary>
    /// 更新同义词组
    /// </summary>
    /// <param name="groupId">同义词组ID</param>
    /// <param name="words">新的词语列表（第一个为标准词）</param>
    Task UpdateGroupAsync(int groupId, IEnumerable<string> words);

    /// <summary>
    /// 删除同义词组
    /// </summary>
    /// <param name="groupId">同义词组ID</param>
    Task DeleteGroupAsync(int groupId);

    /// <summary>
    /// 获取某个词语的所有同义词
    /// </summary>
    /// <param name="word">词语</param>
    /// <returns>同义词列表（包含自身）</returns>
    Task<IReadOnlyList<string>> GetSynonymsAsync(string word);

    /// <summary>
    /// 获取某个词语的标准词
    /// </summary>
    /// <param name="word">词语</param>
    /// <returns>标准词或原词</returns>
    Task<string> GetStandardWordAsync(string word);
}
