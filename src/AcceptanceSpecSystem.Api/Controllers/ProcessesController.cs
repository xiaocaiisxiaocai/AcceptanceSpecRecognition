using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application.Models;
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
        [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync(cancellationToken);
        if (scope == null)
            return Error<PagedData<ProcessDto>>(401, "会话缺少用户上下文");

        var data = await _processAppService.GetPagedAsync(scope.ToAccessContext(), page, pageSize, keyword, cancellationToken);
        return Success(data.ToDto());
    }

    /// <summary>
    /// 获取制程详情
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<ProcessDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ProcessDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ProcessDto>>> GetProcess(
        int id,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync(cancellationToken);
        if (scope == null)
            return Error<ProcessDto>(401, "会话缺少用户上下文");

        var process = await _processAppService.GetByIdAsync(scope.ToAccessContext(), id, cancellationToken);
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
    public async Task<ActionResult<ApiResponse<ProcessDto>>> CreateProcess(
        [FromBody] CreateProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var process = await _processAppService.CreateAsync(request.Name, cancellationToken);
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
    public async Task<ActionResult<ApiResponse<ProcessDto>>> UpdateProcess(
        int id,
        [FromBody] UpdateProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync(cancellationToken);
        if (scope == null)
            return Error<ProcessDto>(401, "会话缺少用户上下文");

        try
        {
            var process = await _processAppService.UpdateAsync(scope.ToAccessContext(), id, request.Name, cancellationToken);
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
    public async Task<ActionResult<ApiResponse>> DeleteProcess(
        int id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await _processAppService.DeleteAsync(id, cancellationToken);
            if (!deleted)
                return Error(404, "制程不存在");

            return Success("删除制程成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 批量删除制程
    /// </summary>
    [HttpPost("batch-delete")]
    [AuditOperation("batch-delete", "process")]
    [ProducesResponseType(typeof(ApiResponse<BatchDeleteResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchDeleteResponseDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<BatchDeleteResponseDto>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<BatchDeleteResponseDto>>> BatchDeleteProcesses(
        [FromBody] BatchDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Ids == null || request.Ids.Count == 0)
            return Error<BatchDeleteResponseDto>(400, "请选择要删除的制程");

        BatchDeleteResultModel result;
        try
        {
            result = await _processAppService.BatchDeleteAsync(request.Ids, cancellationToken);
        }
        catch (ApplicationServiceException ex)
        {
            return Error<BatchDeleteResponseDto>(ex.Code, ex.Message);
        }
        var response = new BatchDeleteResponseDto
        {
            SucceededIds = result.SucceededIds,
            Failures = result.Failures.Select(f => new BatchDeleteFailureDto { Id = f.Id, Reason = f.Reason }).ToList()
        };

        if (result.Failures.Count == 0)
            return Success(response, $"成功删除 {result.SucceededIds.Count} 个制程");

        var failureSummary = string.Join("；", result.Failures.Select(f => $"ID {f.Id}: {f.Reason}"));
        var message = result.SucceededIds.Count > 0
            ? $"成功删除 {result.SucceededIds.Count} 个制程，{result.Failures.Count} 个失败（{failureSummary}）"
            : $"删除失败（{failureSummary}）";

        return Success(response, message);
    }

    /// <summary>
    /// 获取制程的验收规格列表
    /// </summary>
    [HttpGet("{id}/specs")]
    [ProducesResponseType(typeof(ApiResponse<PagedData<AcceptanceSpecListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedData<AcceptanceSpecListItemDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PagedData<AcceptanceSpecListItemDto>>>> GetProcessSpecs(
        int id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync(cancellationToken);
        if (scope == null)
            return Error<PagedData<AcceptanceSpecListItemDto>>(401, "会话缺少用户上下文");

        var data = await _processAppService.GetSpecsAsync(scope.ToAccessContext(), id, page, pageSize, keyword, cancellationToken);
        if (data == null)
            return NotFoundResult<PagedData<AcceptanceSpecListItemDto>>("制程不存在");

        return Success(data.ToDto());
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync(CancellationToken cancellationToken)
    {
        return await SpecDataScopeHelper.ResolveScopeAsync(User, _authDataScopeService, cancellationToken);
    }
}
