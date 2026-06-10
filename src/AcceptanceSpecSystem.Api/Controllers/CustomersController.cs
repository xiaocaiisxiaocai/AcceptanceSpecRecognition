using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 客户管理API控制器
/// </summary>
[Authorize]
public class CustomersController : BaseApiController
{
    private readonly CustomerAppService _customerAppService;
    private readonly IAuthDataScopeService _authDataScopeService;

    /// <summary>
    /// 创建客户控制器实例
    /// </summary>
    public CustomersController(
        CustomerAppService customerAppService,
        IAuthDataScopeService authDataScopeService)
    {
        _customerAppService = customerAppService;
        _authDataScopeService = authDataScopeService;
    }

    /// <summary>
    /// 获取客户列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<CustomerDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<CustomerDto>>>> GetCustomers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<PagedData<CustomerDto>>(401, "会话缺少用户上下文");

        var data = await _customerAppService.GetPagedAsync(scope.ToAccessContext(), page, pageSize, keyword, cancellationToken);
        return Success(data.ToDto());
    }

    /// <summary>
    /// 获取客户详情
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<CustomerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CustomerDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> GetCustomer(
        int id,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<CustomerDto>(401, "会话缺少用户上下文");

        var customer = await _customerAppService.GetByIdAsync(scope.ToAccessContext(), id, cancellationToken);
        if (customer == null)
            return NotFoundResult<CustomerDto>("客户不存在");

        return Success(customer.ToDto());
    }

    /// <summary>
    /// 创建客户
    /// </summary>
    [HttpPost]
    [AuditOperation("create", "customer")]
    [ProducesResponseType(typeof(ApiResponse<CustomerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CustomerDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> CreateCustomer(
        [FromBody] CreateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var customer = await _customerAppService.CreateAsync(request.Name, cancellationToken);
            return Success(customer.ToDto(), "创建客户成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<CustomerDto>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 更新客户
    /// </summary>
    [HttpPut("{id}")]
    [AuditOperation("update", "customer")]
    [ProducesResponseType(typeof(ApiResponse<CustomerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CustomerDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<CustomerDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> UpdateCustomer(
        int id,
        [FromBody] UpdateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<CustomerDto>(401, "会话缺少用户上下文");

        try
        {
            var customer = await _customerAppService.UpdateAsync(scope.ToAccessContext(), id, request.Name, cancellationToken);
            if (customer == null)
                return NotFoundResult<CustomerDto>("客户不存在");

            return Success(customer.ToDto(), "更新客户成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<CustomerDto>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 删除客户
    /// </summary>
    [HttpDelete("{id}")]
    [AuditOperation("delete", "customer")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteCustomer(
        int id,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _customerAppService.DeleteAsync(id, cancellationToken);
        if (!deleted)
            return NotFound(ApiResponse.Error(404, "客户不存在"));

        return Success("删除客户成功");
    }

    /// <summary>
    /// 批量删除客户
    /// </summary>
    [HttpPost("batch-delete")]
    [AuditOperation("batch-delete", "customer")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> BatchDeleteCustomers(
        [FromBody] BatchDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Ids == null || request.Ids.Count == 0)
            return Error(400, "请选择要删除的客户");

        var deletedCount = 0;
        foreach (var id in request.Ids)
        {
            var deleted = await _customerAppService.DeleteAsync(id, cancellationToken);
            if (deleted) deletedCount++;
        }

        return Success($"成功删除 {deletedCount} 个客户");
    }

    /// <summary>
    /// 获取客户的制程列表
    /// </summary>
    [HttpGet("{id}/processes")]
    [ProducesResponseType(typeof(ApiResponse<List<ProcessDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<List<ProcessDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<ProcessDto>>>> GetCustomerProcesses(
        int id,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<List<ProcessDto>>(401, "会话缺少用户上下文");

        var items = await _customerAppService.GetProcessesAsync(scope.ToAccessContext(), id, cancellationToken);
        if (items == null)
            return NotFoundResult<List<ProcessDto>>("客户不存在");

        return Success(items.Select(item => item.ToDto()).ToList());
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync()
    {
        return await SpecDataScopeHelper.ResolveScopeAsync(User, _authDataScopeService);
    }
}
