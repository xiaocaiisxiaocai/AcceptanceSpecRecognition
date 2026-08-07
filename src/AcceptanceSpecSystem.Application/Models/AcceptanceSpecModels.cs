namespace AcceptanceSpecSystem.Application.Models;

public sealed class AcceptanceSpecSummary
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public int? ProcessId { get; set; }

    public int? MachineModelId { get; set; }

    public string ProcessName { get; set; } = string.Empty;

    public string MachineModelName { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string Project { get; set; } = string.Empty;

    public string Specification { get; set; } = string.Empty;

    public string? Acceptance { get; set; }

    public string? Remark { get; set; }

    public long ReferenceCount { get; set; }

    public long ReferenceVersion { get; set; }

    public DateTime? LastReferencedAtUtc { get; set; }

    public DateTime ImportedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? OwnerOrgUnitId { get; set; }

    public int? CreatedByUserId { get; set; }
}

public sealed class SpecGroupSummary
{
    public int CustomerId { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public int? MachineModelId { get; set; }

    public string? MachineModelName { get; set; }

    public int? ProcessId { get; set; }

    public string? ProcessName { get; set; }

    public int SpecCount { get; set; }
}

public sealed class BatchImportSpecItemInput
{
    public string Project { get; set; } = string.Empty;

    public string Specification { get; set; } = string.Empty;

    public string? Acceptance { get; set; }

    public string? Remark { get; set; }
}

public sealed class BatchImportResultModel
{
    public int SuccessCount { get; set; }

    public int FailedCount { get; set; }

    public int TotalCount { get; set; }
}

public sealed class AcceptanceSpecReferenceHistoryModel
{
    public int SpecId { get; set; }

    public long CurrentReferenceVersion { get; set; }

    public long CurrentReferenceCount { get; set; }

    public long RecordedReferenceCount { get; set; }

    public long UntrackedReferenceCount { get; set; }

    public bool IncludePreviousVersions { get; set; }

    public string Sort { get; set; } = "newest";

    public List<AcceptanceSpecReferenceHistoryItemModel> Items { get; set; } = [];

    public int Total { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}

public sealed class AcceptanceSpecReferenceHistoryItemModel
{
    public long Id { get; set; }

    public long? ReferenceOrdinal { get; set; }

    public long ReferenceVersion { get; set; }

    public bool IsCurrentVersion { get; set; }

    public DateTime ReferencedAtUtc { get; set; }
}

public sealed class SpecRemarkReplacePreviewModel
{
    public int AffectedSpecCount { get; set; }

    public int MatchCount { get; set; }

    public string ConfirmationToken { get; set; } = string.Empty;

    public int SamplePage { get; set; }

    public int SamplePageSize { get; set; }

    public int SampleTotal { get; set; }

    public List<SpecRemarkReplaceSampleModel> Samples { get; set; } = [];
}

public sealed class SpecRemarkReplaceSampleModel
{
    public int SpecId { get; set; }

    public string Project { get; set; } = string.Empty;

    public string BeforePreview { get; set; } = string.Empty;

    public string AfterPreview { get; set; } = string.Empty;
}

public sealed class SpecRemarkReplaceResultModel
{
    public int UpdatedSpecCount { get; set; }

    public int ReplacedMatchCount { get; set; }
}

public sealed class SpecDuplicateDetectionResultModel
{
    public int ScannedCount { get; set; }

    public int ExactGroupCount { get; set; }

    public int SimilarGroupCount { get; set; }

    public List<SpecDuplicateGroupModel> ExactGroups { get; set; } = [];

    public List<SpecDuplicateGroupModel> SimilarGroups { get; set; } = [];
}

public sealed class SpecDuplicateGroupModel
{
    public string GroupType { get; set; } = string.Empty;

    public string Project { get; set; } = string.Empty;

    public string SpecificationPreview { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public double SimilarityScore { get; set; }

    public int ItemCount { get; set; }

    public List<SpecDuplicateItemModel> Items { get; set; } = [];
}

public sealed class SpecDuplicateItemModel
{
    public int Id { get; set; }

    public string Project { get; set; } = string.Empty;

    public string Specification { get; set; } = string.Empty;

    public string? Acceptance { get; set; }

    public string? Remark { get; set; }

    public DateTime ImportedAt { get; set; }
}
