using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.DTOs;

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

    public DateTime CreatedAt { get; set; }

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
