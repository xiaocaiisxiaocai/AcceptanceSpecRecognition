using AcceptanceSpecSystem.Application.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 客户用例服务。
/// </summary>
public sealed class CustomerAppService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly AcceptanceSpecQueryService _acceptanceSpecQueryService;
    private readonly ILogger<CustomerAppService> _logger;

    public CustomerAppService(
        IUnitOfWork unitOfWork,
        AcceptanceSpecQueryService acceptanceSpecQueryService,
        ILogger<CustomerAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _acceptanceSpecQueryService = acceptanceSpecQueryService;
        _logger = logger;
    }

    public async Task<PagedResult<CustomerSummary>> GetPagedAsync(
        SpecAccessContext scope,
        int page,
        int pageSize,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _unitOfWork.Customers.Query();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var normalizedKeyword = keyword.Trim();
            query = query.Where(customer => customer.Name.Contains(normalizedKeyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(customer => customer.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(customer => new CustomerSummary
            {
                Id = customer.Id,
                Name = customer.Name,
                CreatedAt = customer.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var processCountByCustomer = await _acceptanceSpecQueryService.GetProcessCountByCustomerAsync(
            scope,
            rows.Select(item => item.Id).ToArray(),
            cancellationToken);
        var specCountByCustomer = await _acceptanceSpecQueryService.GetSpecCountByCustomerAsync(
            scope,
            rows.Select(item => item.Id).ToArray(),
            cancellationToken);

        foreach (var row in rows)
        {
            row.ProcessCount = processCountByCustomer.TryGetValue(row.Id, out var processCount)
                ? processCount
                : 0;
            row.SpecCount = specCountByCustomer.TryGetValue(row.Id, out var specCount)
                ? specCount
                : 0;
        }

        return new PagedResult<CustomerSummary>
        {
            Items = rows,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<CustomerSummary?> GetByIdAsync(
        SpecAccessContext scope,
        int id,
        CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Customers.GetByIdAsync(id, cancellationToken);
        if (customer == null)
            return null;

        var processCountByCustomer = await _acceptanceSpecQueryService.GetProcessCountByCustomerAsync(
            scope,
            [id],
            cancellationToken);
        var specCountByCustomer = await _acceptanceSpecQueryService.GetSpecCountByCustomerAsync(
            scope,
            [id],
            cancellationToken);
        return new CustomerSummary
        {
            Id = customer.Id,
            Name = customer.Name,
            CreatedAt = customer.CreatedAt,
            ProcessCount = processCountByCustomer.TryGetValue(id, out var processCount) ? processCount : 0,
            SpecCount = specCountByCustomer.TryGetValue(id, out var specCount) ? specCount : 0
        };
    }

    public async Task<CustomerSummary> CreateAsync(
        string customerName,
        CancellationToken cancellationToken = default)
    {
        var name = NormalizeRequiredName(customerName, "客户名称不能为空");
        if (await _unitOfWork.Customers.AnyAsync(customer => customer.Name == name))
            throw new ApplicationServiceException(400, "客户名称已存在");

        var customer = new Customer
        {
            Name = name,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Customers.AddAsync(customer);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("创建客户成功: {CustomerId} - {CustomerName}", customer.Id, customer.Name);

        return new CustomerSummary
        {
            Id = customer.Id,
            Name = customer.Name,
            CreatedAt = customer.CreatedAt,
            ProcessCount = 0,
            SpecCount = 0
        };
    }

    public async Task<CustomerSummary?> UpdateAsync(
        SpecAccessContext scope,
        int id,
        string customerName,
        CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Customers.GetByIdAsync(id);
        if (customer == null)
            return null;

        var name = NormalizeRequiredName(customerName, "客户名称不能为空");
        if (await _unitOfWork.Customers.AnyAsync(item => item.Name == name && item.Id != id))
            throw new ApplicationServiceException(400, "客户名称已存在");

        customer.Name = name;
        _unitOfWork.Customers.Update(customer);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("更新客户成功: {CustomerId} - {CustomerName}", customer.Id, customer.Name);

        var processCountByCustomer = await _acceptanceSpecQueryService.GetProcessCountByCustomerAsync(
            scope,
            [id],
            cancellationToken);
        var specCountByCustomer = await _acceptanceSpecQueryService.GetSpecCountByCustomerAsync(
            scope,
            [id],
            cancellationToken);
        return new CustomerSummary
        {
            Id = customer.Id,
            Name = customer.Name,
            CreatedAt = customer.CreatedAt,
            ProcessCount = processCountByCustomer.TryGetValue(id, out var processCount) ? processCount : 0,
            SpecCount = specCountByCustomer.TryGetValue(id, out var specCount) ? specCount : 0
        };
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Customers.GetByIdAsync(id);
        if (customer == null)
            return false;

        var specCount = await _unitOfWork.AcceptanceSpecs.CountAsync(
            spec => spec.CustomerId == id,
            cancellationToken);
        if (specCount > 0)
        {
            throw new ApplicationServiceException(
                409,
                $"该客户下还有 {specCount} 条关联验收规格，无法删除，请先清理关联数据");
        }

        _unitOfWork.Customers.Remove(customer);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw DeleteConflict("该客户下新增了关联验收规格，无法删除，请刷新后重试");
        }

        _logger.LogInformation("删除客户成功: {CustomerId} - {CustomerName}", customer.Id, customer.Name);
        return true;
    }

    /// <summary>
    /// 批量删除客户：整体在一个事务内执行，逐项校验关联规格并单独回报失败原因，
    /// 避免出现"部分静默删除、部分因异常整体回滚"的不一致结果。
    /// </summary>
    public async Task<BatchDeleteResultModel> BatchDeleteAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default)
    {
        var result = new BatchDeleteResultModel();
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var id in ids)
            {
                var customer = await _unitOfWork.Customers.GetByIdAsync(id, cancellationToken);
                if (customer == null)
                {
                    result.Failures.Add(new BatchDeleteFailureModel { Id = id, Reason = "客户不存在" });
                    continue;
                }

                var specCount = await _unitOfWork.AcceptanceSpecs.CountAsync(
                    spec => spec.CustomerId == id,
                    cancellationToken);
                if (specCount > 0)
                {
                    result.Failures.Add(new BatchDeleteFailureModel
                    {
                        Id = id,
                        Reason = $"存在 {specCount} 条关联验收规格，无法删除"
                    });
                    continue;
                }

                _unitOfWork.Customers.Remove(customer);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                result.SucceededIds.Add(id);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw DeleteConflict("删除期间关联验收规格发生变化，请刷新后重试");
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        if (result.SucceededIds.Count > 0)
        {
            _logger.LogInformation("批量删除客户成功: {CustomerIds}", string.Join(",", result.SucceededIds));
        }

        return result;
    }

    private static ApplicationServiceException DeleteConflict(string message) => new(409, message);

    public async Task<List<ProcessSummary>?> GetProcessesAsync(
        SpecAccessContext scope,
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Customers.GetByIdAsync(customerId);
        if (customer == null)
            return null;

        return await _acceptanceSpecQueryService.GetCustomerProcessesAsync(scope, customerId, cancellationToken);
    }

    private static string NormalizeRequiredName(string? value, string message)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ApplicationServiceException(400, message);

        return normalized;
    }
}
