using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Core.Interfaces;

/// <summary>
/// 用户反馈学习服务接口
/// </summary>
public interface IFeedbackLearningService
{
    /// <summary>
    /// 记录用户反馈
    /// </summary>
    Task RecordFeedbackAsync(UserFeedback feedback);

    /// <summary>
    /// 生成同义词建议
    /// </summary>
    Task<List<SynonymSuggestion>> GenerateSynonymSuggestionsAsync();

    /// <summary>
    /// 获取反馈统计
    /// </summary>
    Task<FeedbackStatistics> GetStatisticsAsync();
}
