namespace AcceptanceSpecSystem.Application.Contracts;

public sealed class MatchingTaskStatusDto
{
    public string TaskId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool CanDownload { get; init; }
    public DateTime UpdatedAt { get; init; }
}
