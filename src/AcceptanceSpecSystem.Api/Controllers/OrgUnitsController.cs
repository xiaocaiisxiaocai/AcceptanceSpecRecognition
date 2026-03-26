using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 组织管理控制器
/// </summary>
[Route("api/org-units")]
[Authorize]
public class OrgUnitsController : BaseApiController
{
    private readonly AppDbContext _dbContext;

    public OrgUnitsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
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

        var rootOrgUnit = await SingleOrgUnitService.GetRootOrgUnitAsync(_dbContext, companyId.Value);
        if (rootOrgUnit == null)
            return Success(new List<OrgUnitDto>());

        return Success(new List<OrgUnitDto> { ToDto(rootOrgUnit) });
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

        var rootOrgUnit = await SingleOrgUnitService.GetRootOrgUnitAsync(_dbContext, companyId.Value);
        if (rootOrgUnit == null)
            return Success(new List<OrgUnitDto>());

        return Success(new List<OrgUnitDto> { ToDto(rootOrgUnit) });
    }

    /// <summary>
    /// 创建组织节点
    /// </summary>
    [HttpPost]
    [AuditOperation("create", "org-unit")]
    [ProducesResponseType(typeof(ApiResponse<OrgUnitDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OrgUnitDto>>> Create([FromBody] CreateOrgUnitRequest request)
    {
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error<OrgUnitDto>(401, "会话缺少公司上下文");

        return Error<OrgUnitDto>(400, "系统为单组织模式，不允许新增组织节点");
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

        var entity = await _dbContext.OrgUnits.FirstOrDefaultAsync(o => o.Id == id && o.CompanyId == companyId.Value);
        if (entity == null)
            return Error<OrgUnitDto>(404, "组织节点不存在");

        if (entity.ParentId.HasValue || entity.UnitType != OrgUnitType.Company)
            return Error<OrgUnitDto>(400, "单组织模式下只允许编辑根组织节点");

        var code = NormalizeCode(request.Code);
        if (string.IsNullOrWhiteSpace(code))
            return Error<OrgUnitDto>(400, "组织编码不能为空");

        var duplicated = await _dbContext.OrgUnits.AnyAsync(o =>
            o.CompanyId == companyId.Value &&
            o.Id != id &&
            o.Code == code);
        if (duplicated)
            return Error<OrgUnitDto>(400, "组织编码已存在");

        if (entity.ParentId == null &&
            entity.UnitType == OrgUnitType.Company &&
            !request.IsActive)
        {
            return Error<OrgUnitDto>(400, "公司根节点不允许停用");
        }

        entity.Code = code;
        entity.Name = request.Name.Trim();
        entity.Sort = request.Sort;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.Now;

        await _dbContext.SaveChangesAsync();
        return Success(ToDto(entity), "更新组织节点成功");
    }

    /// <summary>
    /// 删除组织节点
    /// </summary>
    [HttpDelete("{id:int}")]
    [AuditOperation("delete", "org-unit")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!companyId.HasValue)
            return Error(401, "会话缺少公司上下文");

        return Error(400, "系统为单组织模式，不允许删除组织节点");
    }

    private static string NormalizeCode(string code)
    {
        return string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : code.Trim().ToUpperInvariant();
    }

    private static OrgUnitDto ToDto(OrgUnit entity)
    {
        return new OrgUnitDto
        {
            Id = entity.Id,
            ParentId = entity.ParentId,
            UnitType = entity.UnitType,
            Code = entity.Code,
            Name = entity.Name,
            Path = entity.Path,
            Depth = entity.Depth,
            Sort = entity.Sort,
            IsActive = entity.IsActive
        };
    }
}
