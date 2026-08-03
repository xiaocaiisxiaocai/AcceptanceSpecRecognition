using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 验收规格Repository实现
/// </summary>
public class AcceptanceSpecRepository : Repository<AcceptanceSpec>, IAcceptanceSpecRepository
{
    private static readonly Expression<Func<AcceptanceSpec, bool>> HasDuplicateCandidateContent =
        BuildHasDuplicateCandidateContentExpression();

    /// <summary>
    /// 创建AcceptanceSpecRepository实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public AcceptanceSpecRepository(AppDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 获取所有验收规格，并包含 <see cref="AcceptanceSpec.Customer"/> / <see cref="AcceptanceSpec.Process"/> /
    /// <see cref="AcceptanceSpec.MachineModel"/> 导航属性。
    /// 用途：列表页需要展示客户/制程/机型名称时，避免出现空值。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验收规格列表</returns>
    public async Task<IReadOnlyList<AcceptanceSpec>> GetAllWithCustomerAndProcessAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(s => s.Customer)
            .Include(s => s.Process)
            .Include(s => s.MachineModel)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 根据ID获取验收规格，并包含 <see cref="AcceptanceSpec.Customer"/> / <see cref="AcceptanceSpec.Process"/> /
    /// <see cref="AcceptanceSpec.MachineModel"/> 导航属性。
    /// </summary>
    /// <param name="id">验收规格ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验收规格（包含客户与制程）或 null</returns>
    public async Task<AcceptanceSpec?> GetByIdWithCustomerAndProcessAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(s => s.Customer)
            .Include(s => s.Process)
            .Include(s => s.MachineModel)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    /// <summary>
    /// 根据制程ID获取验收规格列表。
    /// </summary>
    /// <param name="processId">制程ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验收规格列表</returns>
    public async Task<IReadOnlyList<AcceptanceSpec>> GetByProcessIdAsync(int processId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(s => s.ProcessId == processId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 根据Word文件ID获取验收规格列表。
    /// </summary>
    /// <param name="wordFileId">Word文件ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验收规格列表</returns>
    public async Task<IReadOnlyList<AcceptanceSpec>> GetByWordFileIdAsync(int wordFileId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(s => s.WordFileId == wordFileId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 按制程ID分页获取验收规格列表（按ID升序）。
    /// </summary>
    /// <param name="processId">制程ID</param>
    /// <param name="pageNumber">页码（从1开始）</param>
    /// <param name="pageSize">每页数量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验收规格列表</returns>
    public async Task<IReadOnlyList<AcceptanceSpec>> GetPagedAsync(int processId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(s => s.ProcessId == processId)
            .OrderBy(s => s.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 获取验收规格及其来源Word文件信息（包含 <see cref="AcceptanceSpec.WordFile"/> 导航属性）。
    /// </summary>
    /// <param name="id">验收规格ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验收规格（包含来源Word文件）或 null</returns>
    public async Task<AcceptanceSpec?> GetWithWordFileAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(s => s.WordFile)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<AcceptanceSpec> Items, int Total)> GetPagedWithFilterAsync(
        AcceptanceSpecQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        var page = options.Page;
        var pageSize = options.PageSize;

        var query = CreateFilteredQuery(options, includeNavigation: true);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(s => s.ImportedAt)
            .ThenByDescending(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AcceptanceSpec>> GetFilteredWithIncludesAsync(
        AcceptanceSpecQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        return await CreateFilteredQuery(options, includeNavigation: true)
            .OrderByDescending(s => s.ImportedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AcceptanceSpecDuplicateCandidate>> GetDuplicateCandidatesAsync(
        AcceptanceSpecQueryOptions options,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (take <= 0)
            return [];

        return await BuildDuplicateCandidatesQuery(options, take)
            .ToListAsync(cancellationToken);
    }

    internal IQueryable<AcceptanceSpecDuplicateCandidate> BuildDuplicateCandidatesQuery(
        AcceptanceSpecQueryOptions options,
        int take)
    {
        var query = CreateFilteredQuery(options, includeNavigation: false);
        query = string.Equals(
                _context.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.InMemory",
                StringComparison.Ordinal)
            ? query.Where(spec =>
                !string.IsNullOrWhiteSpace(spec.Project) &&
                !string.IsNullOrWhiteSpace(spec.Specification))
            : query.Where(HasDuplicateCandidateContent);

        return query
            .OrderBy(spec => spec.Id)
            .Take(take)
            .Select(spec => new AcceptanceSpecDuplicateCandidate
            {
                Id = spec.Id,
                Project = spec.Project,
                Specification = spec.Specification,
                Acceptance = spec.Acceptance,
                Remark = spec.Remark,
                ImportedAt = spec.ImportedAt
            });
    }

    private static Expression<Func<AcceptanceSpec, bool>> BuildHasDuplicateCandidateContentExpression()
    {
        var parameter = Expression.Parameter(typeof(AcceptanceSpec), "spec");
        var replaceMethod = typeof(string).GetMethod(
            nameof(string.Replace),
            [typeof(string), typeof(string)])!;

        static Expression RemoveWhitespace(
            Expression value,
            System.Reflection.MethodInfo method)
        {
            foreach (var whitespace in Enumerable.Range(char.MinValue, char.MaxValue + 1)
                         .Select(code => (char)code)
                         .Where(char.IsWhiteSpace))
            {
                value = Expression.Call(
                    value,
                    method,
                    Expression.Constant(whitespace.ToString()),
                    Expression.Constant(string.Empty));
            }

            return value;
        }

        var project = RemoveWhitespace(
            Expression.Property(parameter, nameof(AcceptanceSpec.Project)),
            replaceMethod);
        var specification = RemoveWhitespace(
            Expression.Property(parameter, nameof(AcceptanceSpec.Specification)),
            replaceMethod);
        var body = Expression.AndAlso(
            Expression.NotEqual(project, Expression.Constant(string.Empty)),
            Expression.NotEqual(specification, Expression.Constant(string.Empty)));
        return Expression.Lambda<Func<AcceptanceSpec, bool>>(body, parameter);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AcceptanceSpecGroupSummaryItem>> GetGroupSummaryWithFilterAsync(
        AcceptanceSpecQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        var query = CreateFilteredQuery(options, includeNavigation: false);

        return await query
            .GroupBy(s => new
            {
                s.CustomerId,
                CustomerName = s.Customer.Name,
                s.MachineModelId,
                MachineModelName = s.MachineModel != null ? s.MachineModel.Name : null,
                s.ProcessId,
                ProcessName = s.Process != null ? s.Process.Name : null
            })
            .Select(g => new AcceptanceSpecGroupSummaryItem
            {
                CustomerId = g.Key.CustomerId,
                CustomerName = g.Key.CustomerName,
                MachineModelId = g.Key.MachineModelId,
                MachineModelName = g.Key.MachineModelName,
                ProcessId = g.Key.ProcessId,
                ProcessName = g.Key.ProcessName,
                SpecCount = g.Count()
            })
            .OrderBy(g => g.CustomerName)
            .ThenBy(g => g.MachineModelName)
            .ThenBy(g => g.ProcessName)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Dictionary<int, int>> GetProcessCountByCustomerAsync(
        AcceptanceSpecQueryOptions scope,
        IReadOnlyCollection<int> customerIds,
        CancellationToken cancellationToken = default)
    {
        if (customerIds.Count == 0)
        {
            return [];
        }

        var groups = await ApplyScope(Query(), scope)
            .Where(spec => customerIds.Contains(spec.CustomerId) && spec.ProcessId.HasValue)
            .GroupBy(spec => spec.CustomerId)
            .Select(group => new
            {
                CustomerId = group.Key,
                ProcessCount = group.Select(item => item.ProcessId!.Value).Distinct().Count()
            })
            .ToListAsync(cancellationToken);

        return groups.ToDictionary(item => item.CustomerId, item => item.ProcessCount);
    }

    /// <inheritdoc />
    public async Task<Dictionary<int, int>> GetSpecCountByCustomerAsync(
        AcceptanceSpecQueryOptions scope,
        IReadOnlyCollection<int> customerIds,
        CancellationToken cancellationToken = default)
    {
        if (customerIds.Count == 0)
        {
            return [];
        }

        var groups = await ApplyScope(Query(), scope)
            .Where(spec => customerIds.Contains(spec.CustomerId))
            .GroupBy(spec => spec.CustomerId)
            .Select(group => new { CustomerId = group.Key, SpecCount = group.Count() })
            .ToListAsync(cancellationToken);

        return groups.ToDictionary(item => item.CustomerId, item => item.SpecCount);
    }

    /// <inheritdoc />
    public async Task<Dictionary<int, int>> GetSpecCountByProcessAsync(
        AcceptanceSpecQueryOptions scope,
        IReadOnlyCollection<int> processIds,
        CancellationToken cancellationToken = default)
    {
        if (processIds.Count == 0)
        {
            return [];
        }

        var groups = await ApplyScope(Query(), scope)
            .Where(spec => spec.ProcessId.HasValue && processIds.Contains(spec.ProcessId.Value))
            .GroupBy(spec => spec.ProcessId!.Value)
            .Select(group => new { ProcessId = group.Key, SpecCount = group.Count() })
            .ToListAsync(cancellationToken);

        return groups.ToDictionary(item => item.ProcessId, item => item.SpecCount);
    }

    /// <inheritdoc />
    public async Task<Dictionary<int, int>> GetSpecCountByMachineModelAsync(
        AcceptanceSpecQueryOptions scope,
        IReadOnlyCollection<int> machineModelIds,
        CancellationToken cancellationToken = default)
    {
        if (machineModelIds.Count == 0)
        {
            return [];
        }

        var groups = await ApplyScope(Query(), scope)
            .Where(spec => spec.MachineModelId.HasValue && machineModelIds.Contains(spec.MachineModelId.Value))
            .GroupBy(spec => spec.MachineModelId!.Value)
            .Select(group => new { MachineModelId = group.Key, SpecCount = group.Count() })
            .ToListAsync(cancellationToken);

        return groups.ToDictionary(item => item.MachineModelId, item => item.SpecCount);
    }

    private IQueryable<AcceptanceSpec> CreateFilteredQuery(AcceptanceSpecQueryOptions options, bool includeNavigation)
    {
        var query = Query();
        if (includeNavigation)
        {
            query = query
                .Include(s => s.Customer)
                .Include(s => s.Process)
                .Include(s => s.MachineModel);
        }

        return ApplyFilters(ApplyScope(query, options), options);
    }

    private static IQueryable<AcceptanceSpec> ApplyScope(IQueryable<AcceptanceSpec> query, AcceptanceSpecQueryOptions options)
    {
        if (options.CompanyId.HasValue)
        {
            var companyId = options.CompanyId.Value;
            query = query.Where(spec => spec.WordFile.CompanyId == companyId);
        }

        if (options.IsAll)
            return query;

        var scopedOrgUnitIds = options.OrgUnitIds.Distinct().ToArray();

        if (options.IncludeSelf && scopedOrgUnitIds.Length > 0)
        {
            return query.Where(s =>
                (s.CreatedByUserId.HasValue && s.CreatedByUserId.Value == options.UserId) ||
                (s.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(s.OwnerOrgUnitId.Value)));
        }

        if (options.IncludeSelf)
        {
            return query.Where(s =>
                s.CreatedByUserId.HasValue && s.CreatedByUserId.Value == options.UserId);
        }

        if (scopedOrgUnitIds.Length > 0)
        {
            return query.Where(s =>
                s.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(s.OwnerOrgUnitId.Value));
        }

        return query.Where(_ => false);
    }

    private static IQueryable<AcceptanceSpec> ApplyFilters(IQueryable<AcceptanceSpec> query, AcceptanceSpecQueryOptions options)
    {
        if (options.ProcessId.HasValue)
        {
            query = query.Where(spec => spec.ProcessId == options.ProcessId.Value);
        }
        else if (options.ProcessIdIsNull == true)
        {
            query = query.Where(spec => spec.ProcessId == null);
        }

        if (options.MachineModelId.HasValue)
        {
            query = query.Where(spec => spec.MachineModelId == options.MachineModelId.Value);
        }
        else if (options.MachineModelIdIsNull == true)
        {
            query = query.Where(spec => spec.MachineModelId == null);
        }

        if (options.CustomerId.HasValue)
        {
            query = query.Where(spec => spec.CustomerId == options.CustomerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(options.Keyword))
        {
            var keyword = options.Keyword.Trim();
            query = query.Where(spec =>
                spec.Project.Contains(keyword) ||
                spec.Specification.Contains(keyword) ||
                (spec.Acceptance != null && spec.Acceptance.Contains(keyword)) ||
                (spec.Remark != null && spec.Remark.Contains(keyword)));
        }

        if (options.ImportedFrom.HasValue)
        {
            query = query.Where(spec => spec.ImportedAt >= options.ImportedFrom.Value);
        }

        if (options.ImportedTo.HasValue)
        {
            query = query.Where(spec => spec.ImportedAt <= options.ImportedTo.Value);
        }

        return query;
    }
}
