using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

public sealed class AcceptanceSpecQueryOptions
{
    public const int MaxPageSize = 1000;

    private int _page = 1;
    private int _pageSize = 20;

    public int UserId { get; init; }

    public int? CompanyId { get; init; }

    public bool IsAll { get; init; }

    public bool IncludeSelf { get; init; }

    public IReadOnlyCollection<int> OrgUnitIds { get; init; } = [];

    public int? CustomerId { get; init; }

    public int? ProcessId { get; init; }

    public int? MachineModelId { get; init; }

    public bool? ProcessIdIsNull { get; init; }

    public bool? MachineModelIdIsNull { get; init; }

    public string? Keyword { get; init; }

    public DateTime? ImportedFrom { get; init; }

    public DateTime? ImportedTo { get; init; }

    public int Page
    {
        get => _page;
        init => _page = Math.Max(1, value);
    }

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = Math.Clamp(value, 1, MaxPageSize);
    }
}

public sealed class AcceptanceSpecDuplicateCandidate
{
    public int Id { get; init; }
    public string Project { get; init; } = string.Empty;
    public string Specification { get; init; } = string.Empty;
    public string? Acceptance { get; init; }
    public string? Remark { get; init; }
    public DateTime ImportedAt { get; init; }
}

public sealed class AcceptanceSpecGroupSummaryItem
{
    public int CustomerId { get; init; }

    public string CustomerName { get; init; } = string.Empty;

    public int? MachineModelId { get; init; }

    public string? MachineModelName { get; init; }

    public int? ProcessId { get; init; }

    public string? ProcessName { get; init; }

    public int SpecCount { get; init; }
}
