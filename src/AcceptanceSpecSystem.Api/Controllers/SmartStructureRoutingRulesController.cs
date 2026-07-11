using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

[Route("api/smart-structure-routing-rules")]
[Authorize]
public sealed class SmartStructureRoutingRulesController : BaseApiController
{
    private readonly ISmartStructureRoutingRuleAppService _appService;

    public SmartStructureRoutingRulesController(ISmartStructureRoutingRuleAppService appService) => _appService = appService;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<SmartStructureRoutingRuleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<SmartStructureRoutingRuleDto>>>> GetAll(
        [FromQuery] bool? enabled = null, CancellationToken cancellationToken = default) =>
        Success(await _appService.GetAllAsync(enabled, cancellationToken));

    [HttpGet("effective")]
    [ProducesResponseType(typeof(ApiResponse<List<SmartStructureRoutingRuleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<SmartStructureRoutingRuleDto>>>> GetEffective(
        [FromQuery] int? customerId = null, CancellationToken cancellationToken = default) =>
        Success(await _appService.GetEffectiveAsync(customerId, cancellationToken));

    [HttpPost]
    [AuditOperation("create", "smart-structure-routing-rule")]
    [ProducesResponseType(typeof(ApiResponse<SmartStructureRoutingRuleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SmartStructureRoutingRuleDto>>> Create(
        [FromBody] CreateSmartStructureRoutingRuleRequest request, CancellationToken cancellationToken = default)
    {
        try { return Success(await _appService.CreateAsync(request, cancellationToken), "创建成功"); }
        catch (ApplicationServiceException ex) { return Error<SmartStructureRoutingRuleDto>(ex.Code, ex.Message); }
    }

    [HttpPut("{id:int}")]
    [AuditOperation("update", "smart-structure-routing-rule")]
    [ProducesResponseType(typeof(ApiResponse<SmartStructureRoutingRuleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SmartStructureRoutingRuleDto>>> Update(
        int id, [FromBody] UpdateSmartStructureRoutingRuleRequest request, CancellationToken cancellationToken = default)
    {
        try { return Success(await _appService.UpdateAsync(id, request, cancellationToken), "更新成功"); }
        catch (ApplicationServiceException ex) { return Error<SmartStructureRoutingRuleDto>(ex.Code, ex.Message); }
    }

    [HttpDelete("{id:int}")]
    [AuditOperation("delete", "smart-structure-routing-rule")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken cancellationToken = default)
    {
        try { await _appService.DeleteAsync(id, cancellationToken); return Success("删除成功"); }
        catch (ApplicationServiceException ex) { return Error(ex.Code, ex.Message); }
    }
}
