namespace AcceptanceSpecRecognition.Core.Models;

/// <summary>
/// 同义词库
/// </summary>
public class SynonymLibrary
{
    public string Version { get; set; } = "1.0";
    public List<SynonymGroup> Groups { get; set; } = new();
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 同义词组
/// </summary>
public class SynonymGroup
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public List<string> Terms { get; set; } = new();
    public bool Bidirectional { get; set; } = true;
}

/// <summary>
/// 扩展后的查询
/// </summary>
public class ExpandedQuery
{
    public string Original { get; set; } = string.Empty;
    public string Expanded { get; set; } = string.Empty;
    public List<SynonymMatch> SynonymsUsed { get; set; } = new();
}

/// <summary>
/// 同义词匹配
/// </summary>
public class SynonymMatch
{
    public string Original { get; set; } = string.Empty;
    public List<string> Synonyms { get; set; } = new();
}
