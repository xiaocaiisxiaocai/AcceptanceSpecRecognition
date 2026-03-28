using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 匹配知识配置仓储接口。
/// </summary>
public interface IMatchingKnowledgeConfigRepository
{
    /// <summary>
    /// 获取当前匹配知识配置。
    /// </summary>
    /// <returns>当前配置；不存在时返回 null。</returns>
    Task<MatchingKnowledgeConfig?> GetConfigAsync();

    /// <summary>
    /// 保存匹配知识配置；若已存在则覆盖单例内容。
    /// </summary>
    /// <param name="config">要保存的配置。</param>
    Task SaveConfigAsync(MatchingKnowledgeConfig config);
}
