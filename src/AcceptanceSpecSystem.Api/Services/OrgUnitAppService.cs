using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 组织管理应用服务。
/// </summary>
public sealed class OrgUnitAppService
{
    private readonly AppDbContext _dbContext;

    public OrgUnitAppService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<OrgUnitDto>> GetTreeAsync(int companyId)
    {
        var rootOrgUnit = await GetRootOrgUnitAsync(companyId);
        return rootOrgUnit == null ? [] : [ToDto(rootOrgUnit)];
    }

    public async Task<List<OrgUnitDto>> GetFlatAsync(int companyId)
    {
        var rootOrgUnit = await GetRootOrgUnitAsync(companyId);
        return rootOrgUnit == null ? [] : [ToDto(rootOrgUnit)];
    }

    public Task<OrgUnitDto> CreateAsync(int companyId, CreateOrgUnitRequest request)
    {
        throw new ApplicationServiceException(400, "系统为单组织模式，不允许新增组织节点");
    }

    public async Task<OrgUnitDto> UpdateAsync(int companyId, int id, UpdateOrgUnitRequest request)
    {
        var entity = await _dbContext.OrgUnits.FirstOrDefaultAsync(o => o.Id == id && o.CompanyId == companyId);
        if (entity == null)
            throw new ApplicationServiceException(404, "组织节点不存在");

        if (entity.ParentId.HasValue || entity.UnitType != OrgUnitType.Company)
            throw new ApplicationServiceException(400, "单组织模式下只允许编辑根组织节点");

        var code = NormalizeCode(request.Code);
        if (string.IsNullOrWhiteSpace(code))
            throw new ApplicationServiceException(400, "组织编码不能为空");

        var duplicated = await _dbContext.OrgUnits.AnyAsync(o =>
            o.CompanyId == companyId &&
            o.Id != id &&
            o.Code == code);
        if (duplicated)
            throw new ApplicationServiceException(400, "组织编码已存在");

        if (entity.ParentId == null &&
            entity.UnitType == OrgUnitType.Company &&
            !request.IsActive)
        {
            throw new ApplicationServiceException(400, "公司根节点不允许停用");
        }

        entity.Code = code;
        entity.Name = request.Name.Trim();
        entity.Sort = request.Sort;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return ToDto(entity);
    }

    public Task DeleteAsync(int companyId, int id)
    {
        throw new ApplicationServiceException(400, "系统为单组织模式，不允许删除组织节点");
    }

    private Task<OrgUnit?> GetRootOrgUnitAsync(int companyId)
    {
        return _dbContext.OrgUnits
            .AsNoTracking()
            .Where(org => org.CompanyId == companyId && org.ParentId == null && org.UnitType == OrgUnitType.Company)
            .OrderBy(org => org.Id)
            .FirstOrDefaultAsync();
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
