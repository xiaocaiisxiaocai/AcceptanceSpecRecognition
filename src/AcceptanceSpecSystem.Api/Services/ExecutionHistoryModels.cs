using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.Services;

internal static class ExecutionHistoryTaskTypes
{
    public const string SmartFill = "smart-fill";
    public const string BatchReply = "batch-reply";
}

internal static class ExecutionHistoryStatuses
{
    public const string Unmatched = "unmatched";
    public const string Skipped = "skipped";
    public const string NotAdopted = "not-adopted";
    public const string Adopted = "adopted";
}

internal sealed class ExecutionHistoryDraft
{
    public string TaskId { get; set; } = string.Empty;

    public string TaskType { get; set; } = string.Empty;

    public int? SourceFileId { get; set; }

    public string SourceFileName { get; set; } = string.Empty;

    public UploadedFileType? SourceFileType { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<ExecutionHistoryFileDto> Files { get; set; } = [];
}
