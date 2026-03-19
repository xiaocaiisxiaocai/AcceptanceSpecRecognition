using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 客户管理API控制器
/// </summary>
[Authorize]
public class CustomersController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly ILogger<CustomersController> _logger;

    /// <summary>
    /// 创建客户控制器实例
    /// </summary>
    public CustomersController(
        IUnitOfWork unitOfWork,
        IAuthDataScopeService authDataScopeService,
        ILogger<CustomersController> logger)
    {
        _unitOfWork = unitOfWork;
        _authDataScopeService = authDataScopeService;
        _logger = logger;
    }

    /// <summary>
    /// 获取客户列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<CustomerDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<CustomerDto>>>> GetCustomers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error<PagedData<CustomerDto>>(401, "会话缺少用户上下文");
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _unitOfWork.Customers.Query();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(c => c.Name.Contains(key));
        }

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new { c.Id, c.Name, c.CreatedAt })
            .ToListAsync();

        // 统计：每个客户在验规中“使用过的制程数量”（distinct ProcessId）
        var customerIds = rows.Select(c => c.Id).ToList();
        var processCountByCustomer = await BuildCustomerProcessCountAsync(customerIds, scope);

        var items = rows.Select(c => new CustomerDto
        {
            Id = c.Id,
            Name = c.Name,
            CreatedAt = c.CreatedAt,
            ProcessCount = processCountByCustomer.TryGetValue(c.Id, out var count) ? count : 0
        }).ToList();

        var pagedData = new PagedData<CustomerDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return Success(pagedData);
    }

    /// <summary>
    /// 获取客户详情
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<CustomerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CustomerDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> GetCustomer(int id)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error<CustomerDto>(401, "会话缺少用户上下文");
        }

        var customer = await _unitOfWork.Customers.GetByIdAsync(id);

        if (customer == null)
        {
            return NotFoundResult<CustomerDto>("客户不存在");
        }

        var dto = new CustomerDto
        {
            Id = customer.Id,
            Name = customer.Name,
            CreatedAt = customer.CreatedAt,
            ProcessCount = await GetCustomerProcessCountAsync(id, scope)
        };

        return Success(dto);
    }

    /// <summary>
    /// 创建客户
    /// </summary>
    [HttpPost]
    [AuditOperation("create", "customer")]
    [ProducesResponseType(typeof(ApiResponse<CustomerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CustomerDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> CreateCustomer([FromBody] CreateCustomerRequest request)
    {
        // 检查名称是否重复
        var exists = await _unitOfWork.Customers.AnyAsync(c => c.Name == request.Name);

        if (exists)
        {
            return Error<CustomerDto>(400, "客户名称已存在");
        }

        var customer = new Customer
        {
            Name = request.Name,
            CreatedAt = DateTime.Now
        };

        await _unitOfWork.Customers.AddAsync(customer);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("创建客户成功: {CustomerId} - {CustomerName}", customer.Id, customer.Name);

        var dto = new CustomerDto
        {
            Id = customer.Id,
            Name = customer.Name,
            CreatedAt = customer.CreatedAt,
            ProcessCount = 0
        };

        return Success(dto, "创建客户成功");
    }

    /// <summary>
    /// 更新客户
    /// </summary>
    [HttpPut("{id}")]
    [AuditOperation("update", "customer")]
    [ProducesResponseType(typeof(ApiResponse<CustomerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CustomerDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<CustomerDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> UpdateCustomer(int id, [FromBody] UpdateCustomerRequest request)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error<CustomerDto>(401, "会话缺少用户上下文");
        }

        var customer = await _unitOfWork.Customers.GetByIdAsync(id);
        if (customer == null)
        {
            return NotFoundResult<CustomerDto>("客户不存在");
        }

        // 检查名称是否与其他客户重复
        var exists = await _unitOfWork.Customers.AnyAsync(c => c.Name == request.Name && c.Id != id);

        if (exists)
        {
            return Error<CustomerDto>(400, "客户名称已存在");
        }

        customer.Name = request.Name;

        _unitOfWork.Customers.Update(customer);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("更新客户成功: {CustomerId} - {CustomerName}", customer.Id, customer.Name);

        var dto = new CustomerDto
        {
            Id = customer.Id,
            Name = customer.Name,
            CreatedAt = customer.CreatedAt,
            ProcessCount = await GetCustomerProcessCountAsync(id, scope)
        };

        return Success(dto, "更新客户成功");
    }

    /// <summary>
    /// 删除客户
    /// </summary>
    [HttpDelete("{id}")]
    [AuditOperation("delete", "customer")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteCustomer(int id)
    {
        var customer = await _unitOfWork.Customers.GetByIdAsync(id);
        if (customer == null)
        {
            return NotFound(ApiResponse.Error(404, "客户不存在"));
        }

        _unitOfWork.Customers.Remove(customer);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("删除客户成功: {CustomerId} - {CustomerName}", customer.Id, customer.Name);

        return Success("删除客户成功");
    }

    /// <summary>
    /// 获取客户的制程列表
    /// </summary>
    [HttpGet("{id}/processes")]
    [ProducesResponseType(typeof(ApiResponse<List<ProcessDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<List<ProcessDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<ProcessDto>>>> GetCustomerProcesses(int id)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error<List<ProcessDto>>(401, "会话缺少用户上下文");
        }

        var customer = await _unitOfWork.Customers.GetByIdAsync(id);
        if (customer == null)
        {
            return NotFoundResult<List<ProcessDto>>("客户不存在");
        }

        // 返回“该客户的验规中使用过的制程列表”（非从属关系）
        var scopedSpecs = SpecDataScopeHelper.ApplyScope(
            await _unitOfWork.AcceptanceSpecs.FindAsync(s => s.CustomerId == id),
            scope);
        var specCountByProcess = scopedSpecs
            .Where(s => s.ProcessId.HasValue)
            .GroupBy(s => s.ProcessId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        var specProcessIds = scopedSpecs
            .Select(s => s.ProcessId)
            .Where(pid => pid.HasValue)
            .Select(pid => pid!.Value)
            .Distinct()
            .ToList();

        var processes = specProcessIds.Count == 0
            ? []
            : await _unitOfWork.Processes.FindAsync(p => specProcessIds.Contains(p.Id));

        var dtoList = processes
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProcessDto
            {
                Id = p.Id,
                Name = p.Name,
                CreatedAt = p.CreatedAt,
                SpecCount = specCountByProcess.TryGetValue(p.Id, out var count) ? count : 0
            })
            .ToList();

        return Success(dtoList);
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync()
    {
        return await SpecDataScopeHelper.ResolveScopeAsync(User, _authDataScopeService);
    }

    private async Task<Dictionary<int, int>> BuildCustomerProcessCountAsync(
        IReadOnlyCollection<int> customerIds,
        DataScopeResult scope)
    {
        if (customerIds.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        var specs = await _unitOfWork.AcceptanceSpecs.FindAsync(
            s => customerIds.Contains(s.CustomerId) && s.ProcessId.HasValue);

        return SpecDataScopeHelper.ApplyScope(specs, scope)
            .Where(s => s.ProcessId.HasValue)
            .GroupBy(s => s.CustomerId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.ProcessId!.Value).Distinct().Count());
    }

    private async Task<int> GetCustomerProcessCountAsync(int customerId, DataScopeResult scope)
    {
        var counts = await BuildCustomerProcessCountAsync([customerId], scope);
        return counts.TryGetValue(customerId, out var count) ? count : 0;
    }
}
