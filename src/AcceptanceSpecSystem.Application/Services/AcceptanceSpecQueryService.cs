using AcceptanceSpecSystem.Application.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 验规复杂只读查询服务。
/// </summary>
public sealed class AcceptanceSpecQueryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IResourceBudgetGovernor _resourceBudgetGovernor;
    private readonly ResourceBudgetOptions _resourceBudgetOptions;

    public AcceptanceSpecQueryService(
        IUnitOfWork unitOfWork,
        IResourceBudgetGovernor resourceBudgetGovernor,
        IOptions<ResourceBudgetOptions> resourceBudgetOptions)
    {
        _unitOfWork = unitOfWork;
        _resourceBudgetGovernor = resourceBudgetGovernor;
        _resourceBudgetOptions = resourceBudgetOptions.Value;
    }

    public async Task<Dictionary<int, int>> GetProcessCountByCustomerAsync(
        SpecAccessContext scope,
        IReadOnlyCollection<int> customerIds,
        CancellationToken cancellationToken = default)
    {
        if (customerIds.Count == 0)
            return new Dictionary<int, int>();

        return await _unitOfWork.AcceptanceSpecs.GetProcessCountByCustomerAsync(
            BuildScopeOptions(scope),
            customerIds,
            cancellationToken);
    }

    public async Task<Dictionary<int, int>> GetSpecCountByCustomerAsync(
        SpecAccessContext scope,
        IReadOnlyCollection<int> customerIds,
        CancellationToken cancellationToken = default)
    {
        if (customerIds.Count == 0)
            return new Dictionary<int, int>();

        return await _unitOfWork.AcceptanceSpecs.GetSpecCountByCustomerAsync(
            BuildScopeOptions(scope),
            customerIds,
            cancellationToken);
    }

    public async Task<Dictionary<int, int>> GetSpecCountByProcessAsync(
        SpecAccessContext scope,
        IReadOnlyCollection<int> processIds,
        CancellationToken cancellationToken = default)
    {
        if (processIds.Count == 0)
            return new Dictionary<int, int>();

        return await _unitOfWork.AcceptanceSpecs.GetSpecCountByProcessAsync(
            BuildScopeOptions(scope),
            processIds,
            cancellationToken);
    }

    public async Task<Dictionary<int, int>> GetSpecCountByMachineModelAsync(
        SpecAccessContext scope,
        IReadOnlyCollection<int> machineModelIds,
        CancellationToken cancellationToken = default)
    {
        if (machineModelIds.Count == 0)
            return new Dictionary<int, int>();

        return await _unitOfWork.AcceptanceSpecs.GetSpecCountByMachineModelAsync(
            BuildScopeOptions(scope),
            machineModelIds,
            cancellationToken);
    }

    public async Task<List<ProcessSummary>> GetCustomerProcessesAsync(
        SpecAccessContext scope,
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var processCounts = await ApplyScope(_unitOfWork.AcceptanceSpecs.Query(), scope)
            .Where(spec => spec.CustomerId == customerId && spec.ProcessId.HasValue)
            .GroupBy(spec => spec.ProcessId!.Value)
            .Select(group => new
            {
                ProcessId = group.Key,
                SpecCount = group.Count()
            })
            .ToListAsync(cancellationToken);

        if (processCounts.Count == 0)
            return [];

        var countByProcessId = processCounts.ToDictionary(item => item.ProcessId, item => item.SpecCount);
        var processIds = countByProcessId.Keys.ToArray();

        // 先拿到有规格的制程 ID，再回查制程基础信息，避免把无关制程带进客户维度列表。
        var processes = await _unitOfWork.Processes.Query()
            .Where(process => processIds.Contains(process.Id))
            .OrderByDescending(process => process.CreatedAt)
            .Select(process => new ProcessSummary
            {
                Id = process.Id,
                Name = process.Name,
                CreatedAt = process.CreatedAt
            })
            .ToListAsync(cancellationToken);

        foreach (var process in processes)
        {
            process.SpecCount = countByProcessId.TryGetValue(process.Id, out var specCount) ? specCount : 0;
        }

        return processes;
    }

    public async Task<PagedResult<AcceptanceSpecSummary>> GetPagedAsync(
        SpecAccessContext scope,
        int page,
        int pageSize,
        string? keyword = null,
        int? customerId = null,
        int? processId = null,
        int? machineModelId = null,
        bool? processIdIsNull = null,
        bool? machineModelIdIsNull = null,
        DateTime? importedFrom = null,
        DateTime? importedTo = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, AcceptanceSpecQueryOptions.MaxPageSize);

        var options = BuildQueryOptions(
            scope,
            keyword,
            customerId,
            processId,
            machineModelId,
            processIdIsNull,
            machineModelIdIsNull,
            importedFrom,
            importedTo,
            page,
            pageSize);

        var (items, total) = await _unitOfWork.AcceptanceSpecs.GetPagedWithFilterAsync(
            options,
            cancellationToken);

        return new PagedResult<AcceptanceSpecSummary>
        {
            Items = items.Select(MapDto).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<List<SpecGroupSummary>> GetGroupSummaryAsync(
        SpecAccessContext scope,
        CancellationToken cancellationToken = default)
    {
        var groups = await _unitOfWork.AcceptanceSpecs.GetGroupSummaryWithFilterAsync(
            BuildQueryOptions(scope),
            cancellationToken);
        return groups
            .Select(group => new SpecGroupSummary
            {
                CustomerId = group.CustomerId,
                CustomerName = group.CustomerName,
                MachineModelId = group.MachineModelId,
                MachineModelName = group.MachineModelName,
                ProcessId = group.ProcessId,
                ProcessName = group.ProcessName,
                SpecCount = group.SpecCount
            })
            .ToList();
    }

    public async Task<SpecDuplicateDetectionResultModel> GetDuplicateGroupsAsync(
        SpecAccessContext scope,
        string? keyword = null,
        int? customerId = null,
        int? processId = null,
        int? machineModelId = null,
        bool? processIdIsNull = null,
        bool? machineModelIdIsNull = null,
        double? minSimilarity = null,
        int? maxGroups = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var resourceLease = await _resourceBudgetGovernor.AcquireAsync(
            ResourceWorkload.HighCostMatching,
            cancellationToken);
        var candidateLimit = _resourceBudgetOptions.MaxDuplicateCandidates;
        var take = candidateLimit == int.MaxValue ? int.MaxValue : candidateLimit + 1;
        var allSpecs = await _unitOfWork.AcceptanceSpecs.GetDuplicateCandidatesAsync(
            BuildQueryOptions(
                scope,
                keyword,
                customerId,
                processId,
                machineModelId,
                processIdIsNull,
                machineModelIdIsNull,
                enforceCompany: true),
            take,
            cancellationToken);

        _resourceBudgetGovernor.ValidateDuplicateCandidates(allSpecs.Count);
        cancellationToken.ThrowIfCancellationRequested();
        return SpecDuplicateDetectionService.Detect(
            allSpecs,
            _resourceBudgetGovernor,
            cancellationToken,
            minSimilarity,
            maxGroups);
    }

    public static AcceptanceSpecSummary MapDto(AcceptanceSpec spec)
    {
        return new AcceptanceSpecSummary
        {
            Id = spec.Id,
            CustomerId = spec.CustomerId,
            ProcessId = spec.ProcessId,
            MachineModelId = spec.MachineModelId,
            ProcessName = spec.Process?.Name ?? string.Empty,
            MachineModelName = spec.MachineModel?.Name ?? string.Empty,
            CustomerName = spec.Customer?.Name ?? string.Empty,
            Project = spec.Project,
            Specification = spec.Specification,
            Acceptance = spec.Acceptance,
            Remark = spec.Remark,
            ImportedAt = spec.ImportedAt,
            OwnerOrgUnitId = spec.OwnerOrgUnitId,
            CreatedByUserId = spec.CreatedByUserId
        };
    }

    private static IQueryable<AcceptanceSpec> ApplyScope(IQueryable<AcceptanceSpec> query, SpecAccessContext scope)
    {
        if (scope.IsAll)
            return query;

        var scopedOrgUnitIds = scope.OrgUnitIds.Distinct().ToArray();

        // 所有聚合查询都先按“本人 + 组织”规则收窄范围，保证统计口径和列表数据一致。
        if (scope.IncludeSelf && scopedOrgUnitIds.Length > 0)
        {
            return query.Where(spec =>
                (spec.CreatedByUserId.HasValue && spec.CreatedByUserId.Value == scope.UserId) ||
                (spec.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(spec.OwnerOrgUnitId.Value)));
        }

        if (scope.IncludeSelf)
        {
            return query.Where(spec =>
                spec.CreatedByUserId.HasValue && spec.CreatedByUserId.Value == scope.UserId);
        }

        if (scopedOrgUnitIds.Length > 0)
        {
            return query.Where(spec =>
                spec.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(spec.OwnerOrgUnitId.Value));
        }

        return query.Where(_ => false);
    }

    private static AcceptanceSpecQueryOptions BuildScopeOptions(SpecAccessContext scope) => BuildQueryOptions(scope);

    private static AcceptanceSpecQueryOptions BuildQueryOptions(
        SpecAccessContext scope,
        string? keyword = null,
        int? customerId = null,
        int? processId = null,
        int? machineModelId = null,
        bool? processIdIsNull = null,
        bool? machineModelIdIsNull = null,
        DateTime? importedFrom = null,
        DateTime? importedTo = null,
        int page = 1,
        int pageSize = 20,
        bool enforceCompany = false)
    {
        return new AcceptanceSpecQueryOptions
        {
            UserId = scope.UserId,
            CompanyId = enforceCompany ? scope.CompanyId : null,
            IsAll = scope.IsAll,
            IncludeSelf = scope.IncludeSelf,
            OrgUnitIds = scope.OrgUnitIds.ToArray(),
            Keyword = keyword,
            CustomerId = customerId,
            ProcessId = processId,
            MachineModelId = machineModelId,
            ProcessIdIsNull = processIdIsNull,
            MachineModelIdIsNull = machineModelIdIsNull,
            Page = page,
            PageSize = pageSize,
            ImportedFrom = importedFrom,
            ImportedTo = importedTo
        };
    }
}
