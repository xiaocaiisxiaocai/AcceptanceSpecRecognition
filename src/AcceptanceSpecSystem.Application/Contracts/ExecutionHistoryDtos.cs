using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Contracts;

/// <summary>
/// 执行记录列表项
/// </summary>
public class ExecutionHistoryListItemDto
{
    public int Id { get; set; }

    public string TaskId { get; set; } = string.Empty;

    public string TaskType { get; set; } = string.Empty;

    public int? SourceFileId { get; set; }

    public string SourceFileName { get; set; } = string.Empty;

    public UploadedFileType? SourceFileType { get; set; }

    public int FileCount { get; set; }

    public int TotalRowCount { get; set; }

    public int MatchedRowCount { get; set; }

    public int AdoptedRowCount { get; set; }

    public int UnmatchedRowCount { get; set; }

    public int SkippedRowCount { get; set; }

    public int NotAdoptedRowCount { get; set; }

    public int ManualSelectedRowCount { get; set; }

    public ExecutionHistorySmartFillSummaryDto? SmartFillSummary { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 执行记录详情
/// </summary>
public class ExecutionHistoryDetailDto
{
    public int Id { get; set; }

    public string TaskId { get; set; } = string.Empty;

    public string TaskType { get; set; } = string.Empty;

    public int? SourceFileId { get; set; }

    public string SourceFileName { get; set; } = string.Empty;

    public UploadedFileType? SourceFileType { get; set; }

    public int FileCount { get; set; }

    public int TotalRowCount { get; set; }

    public int MatchedRowCount { get; set; }

    public int AdoptedRowCount { get; set; }

    public int UnmatchedRowCount { get; set; }

    public int SkippedRowCount { get; set; }

    public int NotAdoptedRowCount { get; set; }

    public int ManualSelectedRowCount { get; set; }

    public ExecutionHistorySmartFillSummaryDto? SmartFillSummary { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<ExecutionHistoryFileDto> Files { get; set; } = [];

    public ExecutionHistorySmartFillPlaybackDto? SmartFillPlayback { get; set; }

    public ExecutionHistoryBatchReplyDetailDto? BatchReplyDetail { get; set; }
}

public class ExecutionHistorySmartFillSummaryDto
{
    public int? ExactMatchedRowCount { get; set; }

    public int? AiMatchedRowCount { get; set; }

    public int? ManualConfirmedRowCount { get; set; }

    public int? ManualEditedRowCount { get; set; }

    public int? NotUsedRowCount { get; set; }

    public bool HasPlaybackArchive { get; set; }
}

public class ExecutionHistorySmartFillPlaybackDto
{
    public int PayloadVersion { get; set; }

    public bool IsLegacy { get; set; }

    /// <summary>
    /// 是否为精简归档：大批量任务超出持久化上限时，剥离重负载（原文/候选明细/证据等），
    /// 但保留逐行分析信号（命中来源/决策/置信度/AI 裁决结论/问题码）。回放据此降级展示。
    /// </summary>
    public bool IsSlimmed { get; set; }

    public bool HasFullArchive { get; set; }

    public string? FullArchiveRelativePath { get; set; }

    public string? LegacyMessage { get; set; }

    public List<ExecutionHistorySmartFillFileDto> Files { get; set; } = [];
}

public class ExecutionHistorySmartFillFileDto
{
    public string FileName { get; set; } = string.Empty;

    public UploadedFileType? FileType { get; set; }

    public List<ExecutionHistorySmartFillSheetDto> Sheets { get; set; } = [];
}

public class ExecutionHistorySmartFillSheetDto
{
    public int SheetIndex { get; set; }

    public string SheetName { get; set; } = string.Empty;

    public List<ExecutionHistorySmartFillRowDto> Rows { get; set; } = [];
}

public class ExecutionHistorySmartFillRowDto
{
    public int RowIndex { get; set; }

    public string SourceProject { get; set; } = string.Empty;

    public string SourceSpecification { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string MatchOrigin { get; set; } = "none";

    public bool IsManualConfirmed { get; set; }

    public bool IsManualEdited { get; set; }

    public List<string> DisplayTags { get; set; } = [];

    public ExecutionHistorySmartFillPreviewSnapshotDto PreviewSnapshot { get; set; } = new();

    public ExecutionHistorySmartFillExecutionSnapshotDto ExecutionSnapshot { get; set; } = new();
}

public class ExecutionHistorySmartFillPreviewSnapshotDto
{
    public string ConfidenceLevel { get; set; } = "none";

    public string? NoMatchReason { get; set; }

    public MatchResultDto? BestMatch { get; set; }
}

public class ExecutionHistorySmartFillExecutionSnapshotDto
{
    public int? SelectedSpecId { get; set; }

    public string? SelectedProject { get; set; }

    public string? SelectedSpecification { get; set; }

    public string? FinalAcceptance { get; set; }

    public string? FinalRemark { get; set; }

    public string? OverrideAcceptance { get; set; }

    public string? OverrideRemark { get; set; }

    public bool ManualConfirmed { get; set; }

    public bool ManualEdited { get; set; }

    public string Status { get; set; } = string.Empty;
}

public class ExecutionHistoryBatchReplyDetailDto
{
    public List<ExecutionHistoryFileDto> Files { get; set; } = [];
}

public class ExecutionHistoryFileDto
{
    public string FileName { get; set; } = string.Empty;

    public UploadedFileType? FileType { get; set; }

    public List<ExecutionHistorySheetDto> Sheets { get; set; } = [];
}

public class ExecutionHistorySheetDto
{
    public int SheetIndex { get; set; }

    public string SheetName { get; set; } = string.Empty;

    public List<ExecutionHistoryRowDto> Rows { get; set; } = [];
}

public class ExecutionHistoryRowDto
{
    public int RowIndex { get; set; }

    public string Project { get; set; } = string.Empty;

    public string Specification { get; set; } = string.Empty;

    public int? MatchedSpecId { get; set; }

    public string? MatchedProject { get; set; }

    public string? MatchedSpecification { get; set; }

    public string? Acceptance { get; set; }

    public string? Remark { get; set; }

    public double ConfidencePercent { get; set; }

    public string Status { get; set; } = string.Empty;

    public bool IsManualSelected { get; set; }

    public int? AcceptanceColumnIndex { get; set; }

    public int? RemarkColumnIndex { get; set; }
}
