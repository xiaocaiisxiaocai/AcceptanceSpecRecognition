using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.DTOs;

public class OperationHistoryDto
{
    public int Id { get; set; }
    public OperationType OperationType { get; set; }
    public string? TargetFile { get; set; }
    public string? Details { get; set; }
    public bool CanUndo { get; set; }
    public DateTime CreatedAt { get; set; }
}

