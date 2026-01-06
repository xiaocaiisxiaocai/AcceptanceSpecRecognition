namespace AcceptanceSpecRecognition.Core.Models;

/// <summary>
/// 关键字库
/// </summary>
public class KeywordLibrary
{
    public string Version { get; set; } = "1.0";
    public List<KeywordEntry> Keywords { get; set; } = new();
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 关键字条目
/// </summary>
public class KeywordEntry
{
    public string Id { get; set; } = string.Empty;
    public string Keyword { get; set; } = string.Empty;
    public HighlightStyle Style { get; set; } = new();
}

/// <summary>
/// 高亮后的文本
/// </summary>
public class HighlightedText
{
    public string Html { get; set; } = string.Empty;
    public string Plain { get; set; } = string.Empty;
    public List<KeywordHit> Keywords { get; set; } = new();
}

/// <summary>
/// 关键字命中
/// </summary>
public class KeywordHit
{
    public string Keyword { get; set; } = string.Empty;
    public string MatchedText { get; set; } = string.Empty;
    public int Position { get; set; }
}
