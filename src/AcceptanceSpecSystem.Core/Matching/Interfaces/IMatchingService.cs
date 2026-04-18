using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Interfaces;

/// <summary>
/// 匹配服务接口
/// </summary>
public interface IMatchingService
{
    /// <summary>
    /// 查找单个文本的最佳匹配
    /// </summary>
    /// <param name="source">源项</param>
    /// <param name="candidates">候选列表</param>
    /// <param name="config">匹配配置</param>
    /// <returns>匹配结果（按得分排序）</returns>
    Task<List<MatchResult>> FindMatchesAsync(
        MatchSource source,
        IEnumerable<MatchCandidate> candidates,
        MatchingConfig? config = null);

    /// <summary>
    /// 批量匹配
    /// </summary>
    /// <param name="sources">源项列表</param>
    /// <param name="candidates">候选列表</param>
    /// <param name="config">匹配配置</param>
    /// <param name="progress">批量匹配进度回调</param>
    /// <returns>批量匹配结果</returns>
    Task<BatchMatchResult> BatchMatchAsync(
        IEnumerable<MatchSource> sources,
        IEnumerable<MatchCandidate> candidates,
        MatchingConfig? config = null,
        IProgress<BatchMatchProgress>? progress = null);
}
