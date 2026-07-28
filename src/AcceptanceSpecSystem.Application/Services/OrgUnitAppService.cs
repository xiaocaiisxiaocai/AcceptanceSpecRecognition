using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
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
    private readonly IUnitOfWork _unitOfWork;
    private readonly AppDbContext _dbContext;

    public OrgUnitAppService(IUnitOfWork unitOfWork, AppDbContext dbContext)
    {
        _unitOfWork = unitOfWork;
        _dbContext = dbContext;
    }

    public async Task<List<OrgUnitDto>> GetTreeAsync(int companyId, CancellationToken cancellationToken = default)
    {
        var entities = await _unitOfWork.OrgUnits.Query()
            .Where(orgUnit => orgUnit.CompanyId == companyId)
            .OrderBy(orgUnit => orgUnit.Depth)
            .ThenBy(orgUnit => orgUnit.Sort)
            .ThenBy(orgUnit => orgUnit.Id)
            .ToListAsync(cancellationToken);
        var nodes = entities.ToDictionary(entity => entity.Id, ToDto);

        foreach (var entity in entities.Where(entity => entity.ParentId.HasValue))
        {
            if (nodes.TryGetValue(entity.ParentId!.Value, out var parent))
                parent.Children.Add(nodes[entity.Id]);
        }

        return entities
            .Where(entity => !entity.ParentId.HasValue)
            .Select(entity => nodes[entity.Id])
            .ToList();
    }

    public async Task<List<OrgUnitDto>> GetFlatAsync(int companyId, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.OrgUnits.Query()
            .Where(orgUnit => orgUnit.CompanyId == companyId)
            .OrderBy(orgUnit => orgUnit.Depth)
            .ThenBy(orgUnit => orgUnit.Sort)
            .ThenBy(orgUnit => orgUnit.Id)
            .Select(orgUnit => new OrgUnitDto
            {
                Id = orgUnit.Id,
                ParentId = orgUnit.ParentId,
                UnitType = orgUnit.UnitType,
                Code = orgUnit.Code,
                Name = orgUnit.Name,
                Path = orgUnit.Path,
                Depth = orgUnit.Depth,
                Sort = orgUnit.Sort,
                IsActive = orgUnit.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<OrgUnitDto> CreateAsync(
        int companyId,
        CreateOrgUnitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.ParentId.HasValue)
            throw new ApplicationServiceException(400, "新增组织节点必须选择上级组织");

        if (!Enum.IsDefined(request.UnitType) || request.UnitType == OrgUnitType.Company)
            throw new ApplicationServiceException(400, "组织类型无效");

        var parent = await _unitOfWork.OrgUnits.FirstOrDefaultAsync(
            orgUnit => orgUnit.Id == request.ParentId.Value && orgUnit.CompanyId == companyId,
            cancellationToken);
        if (parent == null)
            throw new ApplicationServiceException(400, "上级组织不存在");
        if (!parent.IsActive)
            throw new ApplicationServiceException(400, "上级组织已停用");
        if (parent.UnitType == OrgUnitType.Section)
            throw new ApplicationServiceException(400, "课别不能新增下级组织");
        if (request.UnitType <= parent.UnitType)
            throw new ApplicationServiceException(400, "子节点类型必须是上级组织的下级类型");

        var code = NormalizeCode(request.Code);
        var name = NormalizeName(request.Name);
        await EnsureCodeAvailableAsync(companyId, code, null, cancellationToken);

        var entity = new OrgUnit
        {
            CompanyId = companyId,
            ParentId = parent.Id,
            UnitType = request.UnitType,
            Code = code,
            Name = name,
            Path = "/",
            Depth = parent.Depth + 1,
            Sort = request.Sort,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.OrgUnits.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        entity.Path = $"{parent.Path}{entity.Id}/";
        entity.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.OrgUnits.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task<OrgUnitDto> UpdateAsync(
        int companyId,
        int id,
        UpdateOrgUnitRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.OrgUnits.FirstOrDefaultAsync(
            o => o.Id == id && o.CompanyId == companyId,
            cancellationToken);
        if (entity == null)
            throw new ApplicationServiceException(404, "组织节点不存在");

        var code = NormalizeCode(request.Code);
        var name = NormalizeName(request.Name);
        await EnsureCodeAvailableAsync(companyId, code, id, cancellationToken);

        if (entity.ParentId == null &&
            entity.UnitType == OrgUnitType.Company &&
            !request.IsActive)
        {
            throw new ApplicationServiceException(400, "公司根节点不允许停用");
        }

        if (entity.IsActive && !request.IsActive)
        {
            var hasActiveDescendant = await _dbContext.OrgUnits
                .AsNoTracking()
                .AnyAsync(
                    orgUnit =>
                        orgUnit.CompanyId == companyId &&
                        orgUnit.IsActive &&
                        orgUnit.Path.StartsWith(entity.Path) &&
                        orgUnit.Id != entity.Id,
                    cancellationToken);
            if (hasActiveDescendant)
                throw new ApplicationServiceException(400, "请先停用该节点下的所有下级组织");

            var hasAssignedUser = await _dbContext.AuthUserOrgUnits
                .AsNoTracking()
                .AnyAsync(link => link.OrgUnitId == entity.Id, cancellationToken);
            if (hasAssignedUser)
                throw new ApplicationServiceException(400, "该组织仍有关联用户，不能停用");
        }

        entity.Code = code;
        entity.Name = name;
        entity.Sort = request.Sort;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.OrgUnits.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task DeleteAsync(int companyId, int id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.OrgUnits.FirstOrDefaultAsync(
            orgUnit => orgUnit.Id == id && orgUnit.CompanyId == companyId,
            cancellationToken);
        if (entity == null)
            throw new ApplicationServiceException(404, "组织节点不存在");
        if (!entity.ParentId.HasValue || entity.UnitType == OrgUnitType.Company)
            throw new ApplicationServiceException(400, "公司根节点不允许删除");

        if (await _dbContext.OrgUnits.AsNoTracking()
                .AnyAsync(orgUnit => orgUnit.ParentId == id, cancellationToken))
            throw new ApplicationServiceException(400, "该组织仍有下级组织，不能删除");
        if (await _dbContext.AuthUserOrgUnits.AsNoTracking()
                .AnyAsync(link => link.OrgUnitId == id, cancellationToken))
            throw new ApplicationServiceException(400, "该组织仍有关联用户，不能删除");
        if (await _dbContext.AuthRoleDataScopeNodes.AsNoTracking()
                .AnyAsync(link => link.OrgUnitId == id, cancellationToken))
            throw new ApplicationServiceException(400, "该组织仍被角色数据范围引用，不能删除");
        if (await _dbContext.AcceptanceSpecs.AsNoTracking()
                .AnyAsync(specification => specification.OwnerOrgUnitId == id, cancellationToken))
            throw new ApplicationServiceException(400, "该组织仍被验收规格引用，不能删除");
        if (await _dbContext.WordFiles.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(file => file.OwnerOrgUnitId == id, cancellationToken))
            throw new ApplicationServiceException(400, "该组织仍被历史文件引用，不能删除");

        _unitOfWork.OrgUnits.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureCodeAvailableAsync(
        int companyId,
        string code,
        int? excludedId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ApplicationServiceException(400, "组织编码不能为空");

        var duplicated = await _unitOfWork.OrgUnits.AnyAsync(
            orgUnit =>
                orgUnit.CompanyId == companyId &&
                (!excludedId.HasValue || orgUnit.Id != excludedId.Value) &&
                orgUnit.Code == code,
            cancellationToken);
        if (duplicated)
            throw new ApplicationServiceException(400, "组织编码已存在");
    }

    private static string NormalizeCode(string code)
    {
        return string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : code.Trim().ToUpperInvariant();
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ApplicationServiceException(400, "组织名称不能为空");

        return name.Trim();
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
