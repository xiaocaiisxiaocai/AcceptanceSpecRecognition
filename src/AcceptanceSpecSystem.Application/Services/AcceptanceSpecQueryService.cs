using AcceptanceSpecSystem.Application.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 验规复杂只读查询服务。
/// </summary>
public sealed class AcceptanceSpecQueryService
{
    private readonly IUnitOfWork _unitOfWork;

    public AcceptanceSpecQueryService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Dictionary<int, int>> GetProcessCountByCustomerAsync(
        SpecAccessContext scope,
        IReadOnlyCollection<int> customerIds)
    {
        if (customerIds.Count == 0)
            return new Dictionary<int, int>();

        return await ApplyScope(_unitOfWork.AcceptanceSpecs.Query(), scope)
            .Where(spec => customerIds.Contains(spec.CustomerId) && spec.ProcessId.HasValue)
            .GroupBy(spec => spec.CustomerId)
            .Select(group => new
            {
                CustomerId = group.Key,
                ProcessCount = group.Select(item => item.ProcessId!.Value).Distinct().Count()
            })
            .ToDictionaryAsync(item => item.CustomerId, item => item.ProcessCount);
    }

    public async Task<Dictionary<int, int>> GetSpecCountByProcessAsync(
        SpecAccessContext scope,
        IReadOnlyCollection<int> processIds)
    {
        if (processIds.Count == 0)
            return new Dictionary<int, int>();

        return await ApplyScope(_unitOfWork.AcceptanceSpecs.Query(), scope)
            .Where(spec => spec.ProcessId.HasValue && processIds.Contains(spec.ProcessId.Value))
            .GroupBy(spec => spec.ProcessId!.Value)
            .Select(group => new
            {
                ProcessId = group.Key,
                SpecCount = group.Count()
            })
            .ToDictionaryAsync(item => item.ProcessId, item => item.SpecCount);
    }

    public async Task<Dictionary<int, int>> GetSpecCountByMachineModelAsync(
        SpecAccessContext scope,
        IReadOnlyCollection<int> machineModelIds)
    {
        if (machineModelIds.Count == 0)
            return new Dictionary<int, int>();

        return await ApplyScope(_unitOfWork.AcceptanceSpecs.Query(), scope)
            .Where(spec => spec.MachineModelId.HasValue && machineModelIds.Contains(spec.MachineModelId.Value))
            .GroupBy(spec => spec.MachineModelId!.Value)
            .Select(group => new
            {
                MachineModelId = group.Key,
                SpecCount = group.Count()
            })
            .ToDictionaryAsync(item => item.MachineModelId, item => item.SpecCount);
    }

    public async Task<List<ProcessSummary>> GetCustomerProcessesAsync(SpecAccessContext scope, int customerId)
    {
        var processCounts = await ApplyScope(_unitOfWork.AcceptanceSpecs.Query(), scope)
            .Where(spec => spec.CustomerId == customerId && spec.ProcessId.HasValue)
            .GroupBy(spec => spec.ProcessId!.Value)
            .Select(group => new
            {
                ProcessId = group.Key,
                SpecCount = group.Count()
            })
            .ToListAsync();

        if (processCounts.Count == 0)
            return [];

        var countByProcessId = processCounts.ToDictionary(item => item.ProcessId, item => item.SpecCount);
        var processIds = countByProcessId.Keys.ToArray();

        var processes = await _unitOfWork.Processes.Query()
            .Where(process => processIds.Contains(process.Id))
            .OrderByDescending(process => process.CreatedAt)
            .Select(process => new ProcessSummary
            {
                Id = process.Id,
                Name = process.Name,
                CreatedAt = process.CreatedAt
            })
            .ToListAsync();

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
        DateTime? importedTo = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

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

        var (items, total) = await _unitOfWork.AcceptanceSpecs.GetPagedWithFilterAsync(options);

        return new PagedResult<AcceptanceSpecSummary>
        {
            Items = items.Select(MapDto).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<List<SpecGroupSummary>> GetGroupSummaryAsync(SpecAccessContext scope)
    {
        var groups = await _unitOfWork.AcceptanceSpecs.GetGroupSummaryWithFilterAsync(BuildQueryOptions(scope));
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
        int? maxGroups = null)
    {
        var allSpecs = await _unitOfWork.AcceptanceSpecs.GetFilteredWithIncludesAsync(
            BuildQueryOptions(
                scope,
                keyword,
                customerId,
                processId,
                machineModelId,
                processIdIsNull,
                machineModelIdIsNull));

        return SpecDuplicateDetectionService.Detect(allSpecs, minSimilarity, maxGroups);
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
        int pageSize = 20)
    {
        return new AcceptanceSpecQueryOptions
        {
            UserId = scope.UserId,
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
