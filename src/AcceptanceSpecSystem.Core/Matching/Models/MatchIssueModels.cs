namespace AcceptanceSpecSystem.Core.Matching.Models;

/// <summary>
/// 匹配问题项
/// </summary>
public sealed class MatchIssue
{
    /// <summary>
    /// 问题编码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 严重级别
    /// </summary>
    public string Severity { get; set; } = "warning";

    /// <summary>
    /// 问题所属字段
    /// </summary>
    public string? FieldName { get; set; }

    /// <summary>
    /// 源值
    /// </summary>
    public string? SourceValue { get; set; }

    /// <summary>
    /// 候选值
    /// </summary>
    public string? CandidateValue { get; set; }

    /// <summary>
    /// 用户可读说明
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 建议动作
    /// </summary>
    public string? SuggestedAction { get; set; }
}
