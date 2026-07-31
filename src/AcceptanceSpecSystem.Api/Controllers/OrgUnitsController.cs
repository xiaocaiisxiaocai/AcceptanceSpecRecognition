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
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly IBusinessOrgScopeService _businessOrgScopeService;

    public OrgUnitsController(
        IOrgUnitAppService orgUnitAppService,
        IAuthDataScopeService authDataScopeService,
        IBusinessOrgScopeService businessOrgScopeService)
    {
        _orgUnitAppService = orgUnitAppService;
        _authDataScopeService = authDataScopeService;
        _businessOrgScopeService = businessOrgScopeService;
    }

    [HttpGet("business-context")]
    [ProducesResponseType(typeof(ApiResponse<BusinessOrgContextDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<BusinessOrgContextDto>>> GetBusinessContext(
        CancellationToken cancellationToken = default)
    {
        var userId = AuthClaimHelper.GetUserId(User);
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!userId.HasValue || !companyId.HasValue)
        {
            return Error<BusinessOrgContextDto>(401, "会话缺少用户或公司上下文");
        }

        try
        {
            var scope = await _authDataScopeService.GetScopeAsync(
                userId.Value,
                companyId.Value,
                "spec",
                cancellationToken);
            if (scope == null)
            {
                return Error<BusinessOrgContextDto>(401, "会话缺少用户上下文");
            }

            var context = await _businessOrgScopeService.GetContextAsync(
                scope,
                User.IsInRole("admin"),
                cancellationToken);
            return Success(context);
        }
        catch (ApplicationServiceException ex)
        {
            return Error<BusinessOrgContextDto>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 获取组织树
    /// </summary>
    [HttpGet("tree")]
    [ProducesResponseType(typeof(ApiResponse<List<OrgUnitDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<OrgUnitDto>>>> GetTree(
        CancellationToken cancellationToken = default)
    {
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error<List<OrgUnitDto>>(401, "会话缺少公司上下文");

        var items = await _orgUnitAppService.GetTreeAsync(companyId.Value, cancellationToken);
        if (!User.IsInRole("admin"))
        {
            var userId = AuthClaimHelper.GetUserId(User);
            if (!userId.HasValue)
                return Error<List<OrgUnitDto>>(401, "会话缺少用户上下文");
            var scope = await _authDataScopeService.GetScopeAsync(
                userId.Value, companyId.Value, "spec", cancellationToken);
            items = FilterTree(items, scope?.OrgUnitIds ?? []);
        }
        return Success(items);
    }

    /// <summary>
    /// 获取组织平铺列表
    /// </summary>
    [HttpGet("flat")]
    [ProducesResponseType(typeof(ApiResponse<List<OrgUnitDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<OrgUnitDto>>>> GetFlat(
        CancellationToken cancellationToken = default)
    {
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error<List<OrgUnitDto>>(401, "会话缺少公司上下文");

        var items = await _orgUnitAppService.GetFlatAsync(companyId.Value, cancellationToken);
        if (!User.IsInRole("admin"))
        {
            var userId = AuthClaimHelper.GetUserId(User);
            if (!userId.HasValue)
                return Error<List<OrgUnitDto>>(401, "会话缺少用户上下文");
            var scope = await _authDataScopeService.GetScopeAsync(
                userId.Value, companyId.Value, "spec", cancellationToken);
            var allowed = (scope?.OrgUnitIds ?? []).ToHashSet();
            items = items.Where(item => allowed.Contains(item.Id)).ToList();
        }
        return Success(items);
    }

    /// <summary>
    /// 新增组织节点
    /// </summary>
    [HttpPost]
    [AuditOperation("create", "org-unit")]
    [ProducesResponseType(typeof(ApiResponse<OrgUnitDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrgUnitDto>>> Create(
        [FromBody] CreateOrgUnitRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error<OrgUnitDto>(401, "会话缺少公司上下文");

        try
        {
            var item = await _orgUnitAppService.CreateAsync(companyId.Value, request, cancellationToken);
            return Success(item, "新增组织节点成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<OrgUnitDto>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 更新组织节点
    /// </summary>
    [HttpPut("{id:int}")]
    [AuditOperation("update", "org-unit")]
    [ProducesResponseType(typeof(ApiResponse<OrgUnitDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrgUnitDto>>> Update(
        int id,
        [FromBody] UpdateOrgUnitRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error<OrgUnitDto>(401, "会话缺少公司上下文");

        try
        {
            var item = await _orgUnitAppService.UpdateAsync(companyId.Value, id, request, cancellationToken);
            return Success(item, "更新组织节点成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<OrgUnitDto>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 删除组织节点
    /// </summary>
    [HttpDelete("{id:int}")]
    [AuditOperation("delete", "org-unit")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(
        int id,
        CancellationToken cancellationToken = default)
    {
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error<object>(401, "会话缺少公司上下文");

        try
        {
            await _orgUnitAppService.DeleteAsync(companyId.Value, id, cancellationToken);
            return Success<object>(new { }, "删除组织节点成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<object>(ex.Code, ex.Message);
        }
    }

    private static List<OrgUnitDto> FilterTree(
        IEnumerable<OrgUnitDto> source,
        IEnumerable<int> allowedOrgUnitIds)
    {
        var allowed = allowedOrgUnitIds.ToHashSet();
        return source
            .Select(CloneAllowedNode)
            .Where(node => node != null)
            .Cast<OrgUnitDto>()
            .ToList();

        OrgUnitDto? CloneAllowedNode(OrgUnitDto node)
        {
            var children = node.Children
                .Select(CloneAllowedNode)
                .Where(child => child != null)
                .Cast<OrgUnitDto>()
                .ToList();
            if (!allowed.Contains(node.Id) && children.Count == 0)
                return null;

            return new OrgUnitDto
            {
                Id = node.Id,
                ParentId = node.ParentId,
                UnitType = node.UnitType,
                Code = node.Code,
                Name = node.Name,
                Path = node.Path,
                Depth = node.Depth,
                Sort = node.Sort,
                IsActive = node.IsActive,
                Children = children
            };
        }
    }

}
