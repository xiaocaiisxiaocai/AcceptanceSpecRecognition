using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application.Services;
using ApplicationServiceException = AcceptanceSpecSystem.Application.ApplicationServiceException;
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
        [FromQuery] string? keyword = null)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<PagedData<MachineModelDto>>(401, "会话缺少用户上下文");

        var data = await _machineModelAppService.GetPagedAsync(scope.ToAccessContext(), page, pageSize, keyword);
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
    public async Task<ActionResult<ApiResponse<MachineModelDto>>> GetMachineModel(int id)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<MachineModelDto>(401, "会话缺少用户上下文");

        var model = await _machineModelAppService.GetByIdAsync(scope.ToAccessContext(), id);
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
    public async Task<ActionResult<ApiResponse<MachineModelDto>>> CreateMachineModel([FromBody] CreateMachineModelRequest request)
    {
        try
        {
            var model = await _machineModelAppService.CreateAsync(request.Name);
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
    public async Task<ActionResult<ApiResponse<MachineModelDto>>> UpdateMachineModel(int id, [FromBody] UpdateMachineModelRequest request)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<MachineModelDto>(401, "会话缺少用户上下文");

        try
        {
            var model = await _machineModelAppService.UpdateAsync(scope.ToAccessContext(), id, request.Name);
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
    public async Task<ActionResult<ApiResponse>> DeleteMachineModel(int id)
    {
        var deleted = await _machineModelAppService.DeleteAsync(id);
        if (!deleted)
            return NotFound(ApiResponse.Error(404, "机型不存在"));

        return Success("删除机型成功");
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync()
    {
        return await SpecDataScopeHelper.ResolveScopeAsync(User, _authDataScopeService);
    }
}
