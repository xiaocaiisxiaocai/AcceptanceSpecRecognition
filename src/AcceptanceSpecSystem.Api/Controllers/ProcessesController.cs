using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 制程管理API控制器
/// </summary>
[Authorize]
public class ProcessesController : BaseApiController
{
    private readonly ProcessAppService _processAppService;
    private readonly IAuthDataScopeService _authDataScopeService;

    /// <summary>
    /// 创建制程控制器实例
    /// </summary>
    public ProcessesController(
        ProcessAppService processAppService,
        IAuthDataScopeService authDataScopeService)
    {
        _processAppService = processAppService;
        _authDataScopeService = authDataScopeService;
    }

    /// <summary>
    /// 获取制程列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<ProcessDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<ProcessDto>>>> GetProcesses(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<PagedData<ProcessDto>>(401, "会话缺少用户上下文");

        var data = await _processAppService.GetPagedAsync(scope.ToAccessContext(), page, pageSize, keyword);
        return Success(data.ToDto());
    }

    /// <summary>
    /// 获取制程详情
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<ProcessDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ProcessDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ProcessDto>>> GetProcess(int id)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<ProcessDto>(401, "会话缺少用户上下文");

        var process = await _processAppService.GetByIdAsync(scope.ToAccessContext(), id);
        if (process == null)
            return NotFoundResult<ProcessDto>("制程不存在");

        return Success(process.ToDto());
    }

    /// <summary>
    /// 创建制程
    /// </summary>
    [HttpPost]
    [AuditOperation("create", "process")]
    [ProducesResponseType(typeof(ApiResponse<ProcessDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ProcessDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ProcessDto>>> CreateProcess([FromBody] CreateProcessRequest request)
    {
        try
        {
            var process = await _processAppService.CreateAsync(request.Name);
            return Success(process.ToDto(), "创建制程成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<ProcessDto>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 更新制程
    /// </summary>
    [HttpPut("{id}")]
    [AuditOperation("update", "process")]
    [ProducesResponseType(typeof(ApiResponse<ProcessDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ProcessDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ProcessDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ProcessDto>>> UpdateProcess(int id, [FromBody] UpdateProcessRequest request)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<ProcessDto>(401, "会话缺少用户上下文");

        try
        {
            var process = await _processAppService.UpdateAsync(scope.ToAccessContext(), id, request.Name);
            if (process == null)
                return NotFoundResult<ProcessDto>("制程不存在");

            return Success(process.ToDto(), "更新制程成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<ProcessDto>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 删除制程
    /// </summary>
    [HttpDelete("{id}")]
    [AuditOperation("delete", "process")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteProcess(int id)
    {
        var deleted = await _processAppService.DeleteAsync(id);
        if (!deleted)
            return NotFound(ApiResponse.Error(404, "制程不存在"));

        return Success("删除制程成功");
    }

    /// <summary>
    /// 获取制程的验收规格列表
    /// </summary>
    [HttpGet("{id}/specs")]
    [ProducesResponseType(typeof(ApiResponse<PagedData<AcceptanceSpecDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedData<AcceptanceSpecDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PagedData<AcceptanceSpecDto>>>> GetProcessSpecs(
        int id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<PagedData<AcceptanceSpecDto>>(401, "会话缺少用户上下文");

        var data = await _processAppService.GetSpecsAsync(scope.ToAccessContext(), id, page, pageSize, keyword);
        if (data == null)
            return NotFoundResult<PagedData<AcceptanceSpecDto>>("制程不存在");

        return Success(data.ToDto());
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync()
    {
        return await SpecDataScopeHelper.ResolveScopeAsync(User, _authDataScopeService);
    }
}
