using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

public interface IOrgUnitAppService
{
    Task<List<OrgUnitDto>> GetTreeAsync(int companyId, CancellationToken cancellationToken = default);

    Task<List<OrgUnitDto>> GetFlatAsync(int companyId, CancellationToken cancellationToken = default);

    Task<OrgUnitDto> CreateAsync(
        int companyId,
        CreateOrgUnitRequest request,
        CancellationToken cancellationToken = default);

    Task<OrgUnitDto> UpdateAsync(
        int companyId,
        int id,
        UpdateOrgUnitRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(int companyId, int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 组织管理应用服务。
/// </summary>
public sealed class OrgUnitAppService : IOrgUnitAppService
{
    private readonly AppDbContext _dbContext;

    public OrgUnitAppService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<OrgUnitDto>> GetTreeAsync(int companyId, CancellationToken cancellationToken = default)
    {
        var rootOrgUnit = await GetRootOrgUnitAsync(companyId, cancellationToken);
        return rootOrgUnit == null ? [] : [ToDto(rootOrgUnit)];
    }

    public async Task<List<OrgUnitDto>> GetFlatAsync(int companyId, CancellationToken cancellationToken = default)
    {
        var rootOrgUnit = await GetRootOrgUnitAsync(companyId, cancellationToken);
        return rootOrgUnit == null ? [] : [ToDto(rootOrgUnit)];
    }

    public Task<OrgUnitDto> CreateAsync(
        int companyId,
        CreateOrgUnitRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new ApplicationServiceException(400, "系统为单组织模式，不允许新增组织节点");
    }

    public async Task<OrgUnitDto> UpdateAsync(
        int companyId,
        int id,
        UpdateOrgUnitRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.OrgUnits.FirstOrDefaultAsync(
            o => o.Id == id && o.CompanyId == companyId,
            cancellationToken);
        if (entity == null)
            throw new ApplicationServiceException(404, "组织节点不存在");

        if (entity.ParentId.HasValue || entity.UnitType != OrgUnitType.Company)
            throw new ApplicationServiceException(400, "单组织模式下只允许编辑根组织节点");

        var code = NormalizeCode(request.Code);
        if (string.IsNullOrWhiteSpace(code))
            throw new ApplicationServiceException(400, "组织编码不能为空");

        var duplicated = await _dbContext.OrgUnits.AnyAsync(
            o =>
                o.CompanyId == companyId &&
                o.Id != id &&
                o.Code == code,
            cancellationToken);
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

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public Task DeleteAsync(int companyId, int id, CancellationToken cancellationToken = default)
    {
        throw new ApplicationServiceException(400, "系统为单组织模式，不允许删除组织节点");
    }

    private Task<OrgUnit?> GetRootOrgUnitAsync(int companyId, CancellationToken cancellationToken = default)
    {
        return _dbContext.OrgUnits
            .AsNoTracking()
            .Where(org => org.CompanyId == companyId && org.ParentId == null && org.UnitType == OrgUnitType.Company)
            .OrderBy(org => org.Id)
            .FirstOrDefaultAsync(cancellationToken);
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
