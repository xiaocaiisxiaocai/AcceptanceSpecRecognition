using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// Prompt模板Repository接口
/// </summary>
public interface IPromptTemplateRepository : IRepository<PromptTemplate>
{
    /// <summary>
    /// 根据名称获取模板
    /// </summary>
    /// <param name="name">模板名称</param>
    /// <returns>模板或null</returns>
    Task<PromptTemplate?> GetByNameAsync(string name);

    /// <summary>
    /// 获取默认模板
    /// </summary>
    /// <returns>默认模板或null</returns>
    Task<PromptTemplate?> GetDefaultAsync();

    /// <summary>
    /// 设置默认模板
    /// </summary>
    /// <param name="id">模板ID</param>
    Task SetDefaultAsync(int id);

    /// <summary>
    /// 获取或创建默认模板
    /// </summary>
    /// <param name="defaultContent">默认内容</param>
    /// <returns>模板</returns>
    Task<PromptTemplate> GetOrCreateDefaultAsync(string defaultContent);
}
