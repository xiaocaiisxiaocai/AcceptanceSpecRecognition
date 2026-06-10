namespace AcceptanceSpecSystem.Application.Models;

public sealed class CustomerSummary
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public int ProcessCount { get; set; }
}

public sealed class ProcessSummary
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public int SpecCount { get; set; }
}

public sealed class MachineModelSummary
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public int SpecCount { get; set; }
}
