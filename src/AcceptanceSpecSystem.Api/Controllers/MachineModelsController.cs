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
/// 机型管理API控制器
/// </summary>
[Route("api/machine-models")]
[Authorize]
public class MachineModelsController : BaseApiController
{
    private readonly MachineModelAppService _machineModelAppService;
    private readonly IAuthDataScopeService _authDataScopeService;

    /// <summary>
    /// 创建机型控制器实例
    /// </summary>
    public MachineModelsController(
        MachineModelAppService machineModelAppService,
        IAuthDataScopeService authDataScopeService)
    {
        _machineModelAppService = machineModelAppService;
        _authDataScopeService = authDataScopeService;
    }

    /// <summary>
    /// 获取机型列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<MachineModelDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<MachineModelDto>>>> GetMachineModels(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync(cancellationToken);
        if (scope == null)
            return Error<PagedData<MachineModelDto>>(401, "会话缺少用户上下文");

        var data = await _machineModelAppService.GetPagedAsync(scope.ToAccessContext(), page, pageSize, keyword, cancellationToken);
        return Success(new PagedData<MachineModelDto>
        {
            Items = data.Items.Select(item => item.ToDto()).ToList(),
            Total = data.Total,
            Page = data.Page,
            PageSize = data.PageSize
        });
    }

    /// <summary>
    /// 获取机型详情
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<MachineModelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MachineModelDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MachineModelDto>>> GetMachineModel(
        int id,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync(cancellationToken);
        if (scope == null)
            return Error<MachineModelDto>(401, "会话缺少用户上下文");

        var model = await _machineModelAppService.GetByIdAsync(scope.ToAccessContext(), id, cancellationToken);
        if (model == null)
            return NotFoundResult<MachineModelDto>("机型不存在");

        return Success(model.ToDto());
    }

    /// <summary>
    /// 创建机型
    /// </summary>
    [HttpPost]
    [AuditOperation("create", "machine-model")]
    [ProducesResponseType(typeof(ApiResponse<MachineModelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MachineModelDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<MachineModelDto>>> CreateMachineModel(
        [FromBody] CreateMachineModelRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var model = await _machineModelAppService.CreateAsync(request.Name, cancellationToken);
            return Success(model.ToDto(), "创建机型成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<MachineModelDto>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 更新机型
    /// </summary>
    [HttpPut("{id}")]
    [AuditOperation("update", "machine-model")]
    [ProducesResponseType(typeof(ApiResponse<MachineModelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MachineModelDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<MachineModelDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<MachineModelDto>>> UpdateMachineModel(
        int id,
        [FromBody] UpdateMachineModelRequest request,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync(cancellationToken);
        if (scope == null)
            return Error<MachineModelDto>(401, "会话缺少用户上下文");

        try
        {
            var model = await _machineModelAppService.UpdateAsync(scope.ToAccessContext(), id, request.Name, cancellationToken);
            if (model == null)
                return NotFoundResult<MachineModelDto>("机型不存在");

            return Success(model.ToDto(), "更新机型成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<MachineModelDto>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 删除机型
    /// </summary>
    [HttpDelete("{id}")]
    [AuditOperation("delete", "machine-model")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteMachineModel(
        int id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await _machineModelAppService.DeleteAsync(id, cancellationToken);
            if (!deleted)
                return Error(404, "机型不存在");

            return Success("删除机型成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 批量删除机型
    /// </summary>
    [HttpPost("batch-delete")]
    [AuditOperation("batch-delete", "machine-model")]
    [ProducesResponseType(typeof(ApiResponse<BatchDeleteResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchDeleteResponseDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<BatchDeleteResponseDto>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<BatchDeleteResponseDto>>> BatchDeleteMachineModels(
        [FromBody] BatchDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Ids == null || request.Ids.Count == 0)
            return Error<BatchDeleteResponseDto>(400, "请选择要删除的机型");

        BatchDeleteResultModel result;
        try
        {
            result = await _machineModelAppService.BatchDeleteAsync(request.Ids, cancellationToken);
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
            return Success(response, $"成功删除 {result.SucceededIds.Count} 个机型");

        var failureSummary = string.Join("；", result.Failures.Select(f => $"ID {f.Id}: {f.Reason}"));
        var message = result.SucceededIds.Count > 0
            ? $"成功删除 {result.SucceededIds.Count} 个机型，{result.Failures.Count} 个失败（{failureSummary}）"
            : $"删除失败（{failureSummary}）";

        return Success(response, message);
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync(CancellationToken cancellationToken)
    {
        return await SpecDataScopeHelper.ResolveScopeAsync(User, _authDataScopeService, cancellationToken);
    }
}
