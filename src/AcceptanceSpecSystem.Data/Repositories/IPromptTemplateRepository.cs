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
    /// 根据场景获取模板
    /// </summary>
    /// <param name="scene">模板场景</param>
    /// <returns>模板或null</returns>
    Task<PromptTemplate?> GetBySceneAsync(PromptTemplateScene scene);

    /// <summary>
    /// 获取系统模板列表
    /// </summary>
    /// <returns>系统模板</returns>
    Task<List<PromptTemplate>> GetSystemTemplatesAsync();

    /// <summary>
    /// 获取或创建系统模板
    /// </summary>
    /// <param name="scene">场景</param>
    /// <param name="name">系统键</param>
    /// <param name="displayName">展示名称</param>
    /// <param name="defaultContent">默认内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>模板</returns>
    Task<PromptTemplate> GetOrCreateSystemAsync(
        PromptTemplateScene scene,
        string name,
        string displayName,
        string defaultContent,
        CancellationToken cancellationToken = default);
}
