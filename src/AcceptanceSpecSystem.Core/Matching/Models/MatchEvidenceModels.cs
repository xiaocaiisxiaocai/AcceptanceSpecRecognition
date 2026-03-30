namespace AcceptanceSpecSystem.Core.Matching.Models;

/// <summary>
/// 最终匹配决策
/// </summary>
public enum MatchDecision
{
    /// <summary>
    /// 允许自动采用
    /// </summary>
    AutoApply = 1,

    /// <summary>
    /// 需要人工确认
    /// </summary>
    ManualReview = 2,

    /// <summary>
    /// 明确拒绝
    /// </summary>
    Reject = 3
}

/// <summary>
/// 证据关系类型
/// </summary>
public enum EvidenceRelation
{
    Exact = 1,
    Compatible = 2,
    Overlap = 3,
    Conflict = 4,
    AliasSame = 5,
    ParentChild = 6,
    PossiblyRelated = 7
}

/// <summary>
/// 匹配证据摘要
/// </summary>
public sealed class MatchEvidence
{
    /// <summary>
    /// 数值约束证据
    /// </summary>
    public List<NumericConstraintEvidence> NumericConstraints { get; set; } = [];

    /// <summary>
    /// 标识符证据
    /// </summary>
    public List<IdentifierEvidence> Identifiers { get; set; } = [];

    /// <summary>
    /// 实体证据
    /// </summary>
    public List<EntityEvidence> Entities { get; set; } = [];

    /// <summary>
    /// 是否存在硬冲突
    /// </summary>
    public bool HasHardConflict { get; set; }

    /// <summary>
    /// 简要证据摘要
    /// </summary>
    public List<string> Summary { get; set; } = [];

    /// <summary>
    /// 冲突详情
    /// </summary>
    public List<string> Conflicts { get; set; } = [];

    /// <summary>
    /// 需要关注的不确定项
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// 结构化问题列表
    /// </summary>
    public List<MatchIssue> Issues { get; set; } = [];
}

/// <summary>
/// 数值约束证据
/// </summary>
public sealed class NumericConstraintEvidence
{
    public string FieldName { get; set; } = string.Empty;

    public string SourceExpression { get; set; } = string.Empty;

    public string CandidateExpression { get; set; } = string.Empty;

    public EvidenceRelation Relation { get; set; }
}

/// <summary>
/// 标识符证据
/// </summary>
public sealed class IdentifierEvidence
{
    public string IdentifierType { get; set; } = "型号";

    public string SourceValue { get; set; } = string.Empty;

    public string CandidateValue { get; set; } = string.Empty;

    public EvidenceRelation Relation { get; set; }
}

/// <summary>
/// 实体证据
/// </summary>
public sealed class EntityEvidence
{
    public string EntityType { get; set; } = "品牌";

    public string SourceValue { get; set; } = string.Empty;

    public string CandidateValue { get; set; } = string.Empty;

    public string NormalizedSourceValue { get; set; } = string.Empty;

    public string NormalizedCandidateValue { get; set; } = string.Empty;

    public EvidenceRelation Relation { get; set; }
}
