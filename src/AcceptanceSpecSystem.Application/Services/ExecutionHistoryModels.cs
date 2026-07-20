using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

public static class ExecutionHistoryTaskTypes
{
    public const string SmartFill = "smart-fill";
    public const string BatchReply = "batch-reply";
}

public static class ExecutionHistoryStatuses
{
    public const string Unmatched = "unmatched";
    public const string Skipped = "skipped";
    public const string NotAdopted = "not-adopted";
    public const string Adopted = "adopted";
}

public static class ExecutionHistoryMatchOrigins
{
    public const string Exact = "exact";
    public const string Ai = "ai";
    public const string None = "none";
}

public static class ExecutionHistoryDisplayTags
{
    public const string ExactMatch = "完全匹配";
    public const string AiMatch = "AI匹配";
    public const string ManualConfirm = "人工确认";
    public const string ManualWrite = "人工写入";
    public const string NotUsed = "未采用/未匹配";
}

public sealed class ExecutionHistoryDraft
{
    public const int CurrentSmartFillPlaybackVersion = 2;

    public string TaskId { get; set; } = string.Empty;

    public string TaskType { get; set; } = string.Empty;

    public int? SourceFileId { get; set; }

    public string SourceFileName { get; set; } = string.Empty;

    public UploadedFileType? SourceFileType { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<ExecutionHistoryFileDto> Files { get; set; } = [];

    public ExecutionHistorySmartFillSummaryDto? SmartFillSummary { get; set; }

    public ExecutionHistorySmartFillPlaybackDto? SmartFillPlayback { get; set; }

    public ExecutionHistoryBatchReplyDetailDto? BatchReplyDetail { get; set; }
}
