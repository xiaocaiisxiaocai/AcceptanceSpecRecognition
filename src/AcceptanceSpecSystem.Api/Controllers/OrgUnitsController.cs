using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 组织管理控制器
/// </summary>
[Route("api/org-units")]
[Authorize]
public class OrgUnitsController : BaseApiController
{
    private readonly IOrgUnitAppService _orgUnitAppService;

    public OrgUnitsController(IOrgUnitAppService orgUnitAppService)
    {
        _orgUnitAppService = orgUnitAppService;
    }

    /// <summary>
    /// 获取组织树
    /// </summary>
    [HttpGet("tree")]
    [ProducesResponseType(typeof(ApiResponse<List<OrgUnitDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<OrgUnitDto>>>> GetTree()
    {
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error<List<OrgUnitDto>>(401, "会话缺少公司上下文");

        var items = await _orgUnitAppService.GetTreeAsync(companyId.Value);
        return Success(items);
    }

    /// <summary>
    /// 获取组织平铺列表
    /// </summary>
    [HttpGet("flat")]
    [ProducesResponseType(typeof(ApiResponse<List<OrgUnitDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<OrgUnitDto>>>> GetFlat()
    {
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error<List<OrgUnitDto>>(401, "会话缺少公司上下文");

        var items = await _orgUnitAppService.GetFlatAsync(companyId.Value);
        return Success(items);
    }

    /// <summary>
    /// 更新组织节点
    /// </summary>
    [HttpPut("{id:int}")]
    [AuditOperation("update", "org-unit")]
    [ProducesResponseType(typeof(ApiResponse<OrgUnitDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrgUnitDto>>> Update(int id, [FromBody] UpdateOrgUnitRequest request)
    {
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error<OrgUnitDto>(401, "会话缺少公司上下文");

        try
        {
            var item = await _orgUnitAppService.UpdateAsync(companyId.Value, id, request);
            return Success(item, "更新组织节点成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<OrgUnitDto>(ex.Code, ex.Message);
        }
    }

}
