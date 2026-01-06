using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Core.Interfaces;

/// <summary>
/// 匹配引擎接口
/// </summary>
public interface IMatchingEngine
{
    /// <summary>
    /// 单条匹配
    /// </summary>
    Task<MatchResult> MatchAsync(MatchQuery query);

    /// <summary>
    /// 批量匹配
    /// </summary>
    Task<List<MatchResult>> MatchBatchAsync(List<MatchQuery> queries);

    /// <summary>
    /// 更新历史记录索引
    /// </summary>
    Task UpdateIndexAsync(HistoryRecord record);

    /// <summary>
    /// 获取所有历史记录
    /// </summary>
    Task<List<HistoryRecord>> GetHistoryRecordsAsync();

    /// <summary>
    /// 添加历史记录
    /// </summary>
    Task<HistoryRecord> AddHistoryRecordAsync(HistoryRecord record);

    /// <summary>
    /// 更新历史记录
    /// </summary>
    Task UpdateHistoryRecordAsync(HistoryRecord record);

    /// <summary>
    /// 初始化所有历史记录的向量（用于首次部署或数据迁移）
    /// </summary>
    Task<int> InitializeEmbeddingsAsync();

    /// <summary>
    /// 强制重新生成所有历史记录的向量
    /// </summary>
    Task<int> RegenerateAllEmbeddingsAsync();

    /// <summary>
    /// 获取没有向量的记录数量
    /// </summary>
    Task<int> GetRecordsWithoutEmbeddingCountAsync();

    /// <summary>
    /// 删除历史记录
    /// </summary>
    Task<bool> DeleteHistoryRecordAsync(string id);

    /// <summary>
    /// 批量删除历史记录
    /// </summary>
    Task<int> DeleteHistoryRecordsBatchAsync(List<string> ids);
}
