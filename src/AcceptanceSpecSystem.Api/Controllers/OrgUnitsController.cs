using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
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

        var orgUnits = await _dbContext.OrgUnits
            .AsNoTracking()
            .Where(o => o.CompanyId == companyId.Value)
            .OrderBy(o => o.Depth)
            .ThenBy(o => o.Sort)
            .ThenBy(o => o.Id)
            .ToListAsync();

        var dict = orgUnits.ToDictionary(o => o.Id, ToDto);
        var roots = new List<OrgUnitDto>();
        foreach (var orgUnit in orgUnits)
        {
            var dto = dict[orgUnit.Id];
            if (orgUnit.ParentId.HasValue && dict.TryGetValue(orgUnit.ParentId.Value, out var parentDto))
            {
                parentDto.Children.Add(dto);
            }
            else
            {
                roots.Add(dto);
            }
        }

        return Success(roots);
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

        var items = await _dbContext.OrgUnits
            .AsNoTracking()
            .Where(o => o.CompanyId == companyId.Value)
            .OrderBy(o => o.Depth)
            .ThenBy(o => o.Sort)
            .ThenBy(o => o.Id)
            .Select(o => new OrgUnitDto
            {
                Id = o.Id,
                ParentId = o.ParentId,
                UnitType = o.UnitType,
                Code = o.Code,
                Name = o.Name,
                Path = o.Path,
                Depth = o.Depth,
                Sort = o.Sort,
                IsActive = o.IsActive
            })
            .ToListAsync();

        return Success(items);
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

        var code = NormalizeCode(request.Code);
        if (string.IsNullOrWhiteSpace(code))
            return Error<OrgUnitDto>(400, "组织编码不能为空");

        if (await _dbContext.OrgUnits.AnyAsync(o => o.CompanyId == companyId.Value && o.Code == code))
            return Error<OrgUnitDto>(400, "组织编码已存在");

        OrgUnit? parent = null;
        if (request.ParentId.HasValue)
        {
            parent = await _dbContext.OrgUnits.FirstOrDefaultAsync(o =>
                o.Id == request.ParentId.Value &&
                o.CompanyId == companyId.Value);
            if (parent == null)
                return Error<OrgUnitDto>(400, "上级组织不存在");
        }

        if (!request.ParentId.HasValue && request.UnitType != OrgUnitType.Company)
            return Error<OrgUnitDto>(400, "非公司节点必须指定上级组织");

        var now = DateTime.Now;
        var entity = new OrgUnit
        {
            CompanyId = companyId.Value,
            ParentId = request.ParentId,
            UnitType = request.UnitType,
            Code = code,
            Name = request.Name.Trim(),
            Path = "/",
            Depth = parent?.Depth + 1 ?? 0,
            Sort = request.Sort,
            IsActive = request.IsActive,
            CreatedAt = now
        };

        await _dbContext.OrgUnits.AddAsync(entity);
        await _dbContext.SaveChangesAsync();

        entity.Path = $"{parent?.Path ?? "/"}{entity.Id}/";
        entity.UpdatedAt = now;
        await _dbContext.SaveChangesAsync();

        return Success(ToDto(entity), "创建组织节点成功");
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

        var code = NormalizeCode(request.Code);
        if (string.IsNullOrWhiteSpace(code))
            return Error<OrgUnitDto>(400, "组织编码不能为空");

        var duplicated = await _dbContext.OrgUnits.AnyAsync(o =>
            o.CompanyId == companyId.Value &&
            o.Id != id &&
            o.Code == code);
        if (duplicated)
            return Error<OrgUnitDto>(400, "组织编码已存在");

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

        var entity = await _dbContext.OrgUnits.FirstOrDefaultAsync(o => o.Id == id && o.CompanyId == companyId.Value);
        if (entity == null)
            return Error(404, "组织节点不存在");

        var hasChildren = await _dbContext.OrgUnits.AnyAsync(o => o.ParentId == id);
        if (hasChildren)
            return Error(400, "存在下级组织，无法删除");

        var referencedByUser = await _dbContext.AuthUserOrgUnits.AnyAsync(x => x.OrgUnitId == id);
        if (referencedByUser)
            return Error(400, "组织节点已被用户引用，无法删除");

        var referencedByScope = await _dbContext.AuthRoleDataScopeNodes.AnyAsync(x => x.OrgUnitId == id);
        if (referencedByScope)
            return Error(400, "组织节点已被数据范围引用，无法删除");

        if (entity.UnitType == OrgUnitType.Company)
            return Error(400, "公司根节点不允许删除");

        _dbContext.OrgUnits.Remove(entity);
        await _dbContext.SaveChangesAsync();

        return Success("删除组织节点成功");
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
