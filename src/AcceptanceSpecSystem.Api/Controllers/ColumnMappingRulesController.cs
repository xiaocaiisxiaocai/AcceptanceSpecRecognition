using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

[Route("api/column-mapping-rules")]
[Authorize]
public class ColumnMappingRulesController : BaseApiController
{
    private readonly IColumnMappingRuleAppService _appService;

    public ColumnMappingRulesController(IColumnMappingRuleAppService appService) => _appService = appService;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<ColumnMappingRuleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ColumnMappingRuleDto>>>> GetAll(
        [FromQuery] bool? enabled = null, CancellationToken cancellationToken = default) =>
        Success(await _appService.GetAllAsync(enabled, cancellationToken));

    [HttpGet("effective")]
    [ProducesResponseType(typeof(ApiResponse<List<ColumnMappingRuleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ColumnMappingRuleDto>>>> GetEffective(
        [FromQuery] int? customerId = null, CancellationToken cancellationToken = default) =>
        Success(await _appService.GetEffectiveAsync(customerId, cancellationToken));

    [HttpPost("restore-defaults")]
    [AuditOperation("restore-defaults", "column-mapping-rule")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> RestoreDefaults(
        [FromQuery] ColumnMappingTargetField? targetField = null, CancellationToken cancellationToken = default)
    {
        var added = await _appService.RestoreDefaultsAsync(targetField, cancellationToken);
        return Success<object>(new { added }, "默认词已恢复");
    }

    [HttpPost]
    [AuditOperation("create", "column-mapping-rule")]
    [ProducesResponseType(typeof(ApiResponse<ColumnMappingRuleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ColumnMappingRuleDto>>> Create(
        [FromBody] CreateColumnMappingRuleRequest request, CancellationToken cancellationToken = default)
    {
        try { return Success(await _appService.CreateAsync(request, cancellationToken), "创建成功"); }
        catch (ApplicationServiceException ex) { return Error<ColumnMappingRuleDto>(ex.Code, ex.Message); }
    }

    [HttpPut("{id:int}")]
    [AuditOperation("update", "column-mapping-rule")]
    [ProducesResponseType(typeof(ApiResponse<ColumnMappingRuleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ColumnMappingRuleDto>>> Update(
        int id, [FromBody] UpdateColumnMappingRuleRequest request, CancellationToken cancellationToken = default)
    {
        try { return Success(await _appService.UpdateAsync(id, request, cancellationToken), "更新成功"); }
        catch (ApplicationServiceException ex) { return Error<ColumnMappingRuleDto>(ex.Code, ex.Message); }
    }

    [HttpDelete("{id:int}")]
    [AuditOperation("delete", "column-mapping-rule")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken cancellationToken = default)
    {
        try { await _appService.DeleteAsync(id, cancellationToken); return Success("删除成功"); }
        catch (ApplicationServiceException ex) { return Error(ex.Code, ex.Message); }
    }
}
