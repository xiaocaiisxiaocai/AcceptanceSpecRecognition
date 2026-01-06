using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Core.Services;

/// <summary>
/// 用户反馈学习服务 - 从用户反馈中学习并生成同义词建议
/// </summary>
public class FeedbackLearningService : IFeedbackLearningService
{
    private readonly IJsonStorageService _storageService;
    private readonly IAuditLogger _auditLogger;
    private readonly List<UserFeedback> _feedbackCache = new();
    private const string FeedbackFile = "user_feedback.json";

    public FeedbackLearningService(IJsonStorageService storageService, IAuditLogger auditLogger)
    {
        _storageService = storageService;
        _auditLogger = auditLogger;
        LoadFeedbackAsync().Wait();
    }

    private async Task LoadFeedbackAsync()
    {
        try
        {
            var store = await _storageService.ReadAsync<FeedbackStore>(FeedbackFile);
            if (store?.Feedbacks != null)
            {
                _feedbackCache.Clear();
                _feedbackCache.AddRange(store.Feedbacks);
            }
        }
        catch
        {
            // 文件不存在时忽略
        }
    }

    public async Task RecordFeedbackAsync(UserFeedback feedback)
    {
        feedback.Id = $"fb_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}".Substring(0, 32);
        feedback.Timestamp = DateTime.UtcNow;
        
        _feedbackCache.Add(feedback);

        await _storageService.WriteAsync(FeedbackFile, new FeedbackStore { Feedbacks = _feedbackCache });

        await _auditLogger.LogUserActionAsync(new UserActionLogEntry
        {
            Action = "record_feedback",
            RecordId = feedback.Id,
            Details = $"用户反馈: {feedback.FeedbackType} - {feedback.QueryText}"
        });
    }

    public async Task<List<SynonymSuggestion>> GenerateSynonymSuggestionsAsync()
    {
        var suggestions = new List<SynonymSuggestion>();

        // 分析用户修正的匹配
        var corrections = _feedbackCache
            .Where(f => f.FeedbackType == FeedbackType.Correction && !string.IsNullOrEmpty(f.CorrectedSpec))
            .GroupBy(f => new { f.QueryText, f.CorrectedSpec })
            .Where(g => g.Count() >= 2) // 至少出现2次
            .ToList();

        foreach (var group in corrections)
        {
            var queryTerms = ExtractKeyTerms(group.Key.QueryText);
            var correctedTerms = ExtractKeyTerms(group.Key.CorrectedSpec!);

            // 找出不同的术语作为同义词候选
            var differentTerms = queryTerms.Except(correctedTerms).ToList();
            var matchingTerms = correctedTerms.Except(queryTerms).ToList();

            if (differentTerms.Any() && matchingTerms.Any())
            {
                suggestions.Add(new SynonymSuggestion
                {
                    SourceTerm = differentTerms.First(),
                    SuggestedSynonym = matchingTerms.First(),
                    Confidence = Math.Min(1.0f, group.Count() * 0.2f),
                    OccurrenceCount = group.Count(),
                    Source = "user_correction"
                });
            }
        }

        // 分析确认的匹配，找出高频配对
        var confirmations = _feedbackCache
            .Where(f => f.FeedbackType == FeedbackType.Confirm)
            .GroupBy(f => new { f.QueryText, f.SelectedSpec })
            .Where(g => g.Count() >= 3)
            .ToList();

        foreach (var group in confirmations)
        {
            var queryTerms = ExtractKeyTerms(group.Key.QueryText);
            var selectedTerms = ExtractKeyTerms(group.Key.SelectedSpec ?? "");

            // 找出可能的同义关系
            foreach (var qt in queryTerms)
            {
                foreach (var st in selectedTerms)
                {
                    if (qt != st && IsPotentialSynonym(qt, st))
                    {
                        suggestions.Add(new SynonymSuggestion
                        {
                            SourceTerm = qt,
                            SuggestedSynonym = st,
                            Confidence = Math.Min(1.0f, group.Count() * 0.15f),
                            OccurrenceCount = group.Count(),
                            Source = "user_confirmation"
                        });
                    }
                }
            }
        }

        return await Task.FromResult(suggestions
            .GroupBy(s => new { s.SourceTerm, s.SuggestedSynonym })
            .Select(g => new SynonymSuggestion
            {
                SourceTerm = g.Key.SourceTerm,
                SuggestedSynonym = g.Key.SuggestedSynonym,
                Confidence = g.Max(s => s.Confidence),
                OccurrenceCount = g.Sum(s => s.OccurrenceCount),
                Source = g.First().Source
            })
            .OrderByDescending(s => s.Confidence)
            .Take(20)
            .ToList());
    }

    public async Task<FeedbackStatistics> GetStatisticsAsync()
    {
        var stats = new FeedbackStatistics
        {
            TotalFeedbacks = _feedbackCache.Count,
            ConfirmCount = _feedbackCache.Count(f => f.FeedbackType == FeedbackType.Confirm),
            CorrectionCount = _feedbackCache.Count(f => f.FeedbackType == FeedbackType.Correction),
            RejectCount = _feedbackCache.Count(f => f.FeedbackType == FeedbackType.Reject),
            LastFeedbackTime = _feedbackCache.Any() ? _feedbackCache.Max(f => f.Timestamp) : null
        };

        // 计算确认率
        if (stats.TotalFeedbacks > 0)
        {
            stats.ConfirmRate = (float)stats.ConfirmCount / stats.TotalFeedbacks;
        }

        // 按日期统计
        stats.DailyStats = _feedbackCache
            .GroupBy(f => f.Timestamp.Date)
            .OrderByDescending(g => g.Key)
            .Take(30)
            .Select(g => new DailyFeedbackStat
            {
                Date = g.Key,
                Count = g.Count(),
                ConfirmCount = g.Count(f => f.FeedbackType == FeedbackType.Confirm),
                CorrectionCount = g.Count(f => f.FeedbackType == FeedbackType.Correction)
            })
            .ToList();

        return await Task.FromResult(stats);
    }

    private List<string> ExtractKeyTerms(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();

        // 简单的术语提取：按空格和标点分割，过滤短词
        var terms = text
            .Split(new[] { ' ', ',', '，', '、', '/', '\\', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2)
            .Select(t => t.ToLowerInvariant())
            .Distinct()
            .ToList();

        return terms;
    }

    private bool IsPotentialSynonym(string term1, string term2)
    {
        // 简单的同义词判断规则
        // 1. 长度相近
        if (Math.Abs(term1.Length - term2.Length) > 5) return false;

        // 2. 有共同字符
        var commonChars = term1.Intersect(term2).Count();
        var minLength = Math.Min(term1.Length, term2.Length);
        if (commonChars < minLength * 0.3) return false;

        return true;
    }
}
