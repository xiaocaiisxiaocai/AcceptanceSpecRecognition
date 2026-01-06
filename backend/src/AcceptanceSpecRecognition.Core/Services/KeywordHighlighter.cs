using System.Text.RegularExpressions;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Core.Services;

/// <summary>
/// 关键字高亮器实现
/// </summary>
public class KeywordHighlighter : IKeywordHighlighter
{
    private readonly IJsonStorageService _storage;
    private KeywordLibrary _library;
    private readonly Dictionary<string, KeywordEntry> _keywordMap = new();

    public KeywordHighlighter(IJsonStorageService storage)
    {
        _storage = storage;
        _library = new KeywordLibrary();
        LoadKeywordLibrary("./data/keywords.json");
    }

    public void LoadKeywordLibrary(string path)
    {
        var library = _storage.ReadAsync<KeywordLibrary>(path).GetAwaiter().GetResult();
        if (library != null)
        {
            _library = library;
            BuildKeywordMap();
        }
    }

    private void BuildKeywordMap()
    {
        _keywordMap.Clear();
        foreach (var entry in _library.Keywords)
        {
            // 添加关键字
            _keywordMap[entry.Keyword.ToLower()] = entry;
        }
    }

    public HighlightedText Highlight(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new HighlightedText { Html = text, Plain = text };
        }

        var hits = new List<KeywordHit>();
        var html = text;

        // 按关键字长度降序排序，优先匹配长关键字
        var sortedKeywords = _keywordMap.Keys.OrderByDescending(k => k.Length).ToList();

        foreach (var keyword in sortedKeywords)
        {
            var entry = _keywordMap[keyword];
            var pattern = Regex.Escape(keyword);
            var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                hits.Add(new KeywordHit
                {
                    Keyword = entry.Keyword,
                    MatchedText = match.Value,
                    Position = match.Index
                });
            }
        }

        // 去重（同一位置只保留一个）
        hits = hits.GroupBy(h => h.Position)
                   .Select(g => g.First())
                   .OrderBy(h => h.Position)
                   .ToList();

        // 从后向前替换，避免位置偏移
        foreach (var hit in hits.OrderByDescending(h => h.Position))
        {
            var entry = _keywordMap[hit.MatchedText.ToLower()];
            var style = entry.Style;
            var replacement = $"<span style=\"color:{style.Color};background-color:{style.BackgroundColor};font-weight:{style.FontWeight ?? "normal"}\">{hit.MatchedText}</span>";
            
            html = html.Substring(0, hit.Position) + replacement + html.Substring(hit.Position + hit.MatchedText.Length);
        }

        return new HighlightedText
        {
            Html = html,
            Plain = text,
            Keywords = hits
        };
    }

    public void AddKeyword(string keyword, HighlightStyle style)
    {
        var entry = new KeywordEntry
        {
            Id = $"kw_{Guid.NewGuid():N}",
            Keyword = keyword,
            Style = style
        };

        _library.Keywords.Add(entry);

        // 更新映射
        _keywordMap[keyword.ToLower()] = entry;

        // 保存
        _storage.WriteAsync("./data/keywords.json", _library).GetAwaiter().GetResult();
    }

    public KeywordLibrary GetLibrary()
    {
        return _library;
    }
}
