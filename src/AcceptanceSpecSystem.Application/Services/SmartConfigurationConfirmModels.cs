using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 智能结构确认命令。
/// </summary>
public sealed class SmartConfigurationConfirmCommand
{
    public int CustomerId { get; init; }

    public string? TemplateName { get; init; }

    public IReadOnlyList<string> Headers { get; init; } = [];

    public int? ProjectColumnIndex { get; init; }

    public int SpecificationColumnIndex { get; init; }

    public int? AcceptanceColumnIndex { get; init; }

    public int? RemarkColumnIndex { get; init; }

    public int HeaderRowIndex { get; init; }

    public int HeaderRowCount { get; init; } = 1;

    public int DataStartRowIndex { get; init; } = 1;

    public int? DataEndRowIndex { get; init; }

    public bool IsSpecificationOnly { get; init; }

    public string? TableKind { get; init; }

    public string? Recommendation { get; init; }

    public bool UserModifiedStructure { get; init; }

    public IReadOnlyList<SmartConfigurationLearnedColumn> LearnedColumns { get; init; } = [];
}

/// <summary>
/// 用户确认后的表头学习项。
/// </summary>
public sealed class SmartConfigurationLearnedColumn
{
    public string Header { get; init; } = string.Empty;

    public ColumnMappingTargetField TargetField { get; init; }
}

/// <summary>
/// 智能结构确认沉淀结果。
/// </summary>
public sealed class SmartConfigurationConfirmResult
{
    public bool TemplateSaved { get; init; }

    public int TemplateId { get; init; }

    public int LearnedRuleCount { get; init; }

    public int LearnedRoutingRuleCount { get; init; }

    public int PromotedGlobalRuleCount { get; init; }

    public bool LearningSucceeded { get; init; } = true;
}
