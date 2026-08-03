using System.Data;
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

    Task<OrgUnitDto> MoveAsync(
        int companyId,
        int id,
        MoveOrgUnitRequest request,
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

    public async Task<OrgUnitDto> MoveAsync(
        int companyId,
        int id,
        MoveOrgUnitRequest request,
        CancellationToken cancellationToken = default)
    {
        await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var companyUnits = await _unitOfWork.OrgUnits.Query(asNoTracking: false)
                .Where(orgUnit => orgUnit.CompanyId == companyId)
                .OrderBy(orgUnit => orgUnit.Depth)
                .ThenBy(orgUnit => orgUnit.Id)
                .ToListAsync(cancellationToken);
            var unitsById = companyUnits.ToDictionary(orgUnit => orgUnit.Id);
            var source = unitsById.GetValueOrDefault(id);
            if (source == null)
                throw new ApplicationServiceException(404, "组织节点不存在");
            if (!source.ParentId.HasValue || source.UnitType == OrgUnitType.Company)
                throw new ApplicationServiceException(400, "公司根节点不允许移动");

            var newParent = unitsById.GetValueOrDefault(request.NewParentId);
            if (newParent == null)
                throw new ApplicationServiceException(400, "新的上级组织不存在");

            if (source.ParentId == newParent.Id)
            {
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                return ToDto(source);
            }

            ValidateLineagePath(source, unitsById);
            ValidateLineagePath(newParent, unitsById);
            if (!newParent.IsActive)
                throw new ApplicationServiceException(400, "新的上级组织已停用");

            var sourceParent = unitsById.GetValueOrDefault(source.ParentId.Value);
            if (sourceParent == null ||
                source.Path != $"{sourceParent.Path}{source.Id}/" ||
                source.Depth != sourceParent.Depth + 1)
            {
                throw new ApplicationServiceException(409, "组织路径数据异常，请刷新后重试");
            }

            var childrenByParent = companyUnits
                .Where(orgUnit => orgUnit.ParentId.HasValue)
                .GroupBy(orgUnit => orgUnit.ParentId!.Value)
                .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Id).ToList());
            var subtree = new List<OrgUnit>();
            var visitedIds = new HashSet<int>();
            var queue = new Queue<OrgUnit>();
            queue.Enqueue(source);
            while (queue.Count > 0)
            {
                var orgUnit = queue.Dequeue();
                if (!visitedIds.Add(orgUnit.Id))
                    throw new ApplicationServiceException(409, "组织层级存在循环，请修复后重试");

                ValidatePath(orgUnit);
                subtree.Add(orgUnit);
                if (!childrenByParent.TryGetValue(orgUnit.Id, out var children))
                    continue;

                foreach (var child in children)
                {
                    if (child.Path != $"{orgUnit.Path}{child.Id}/" ||
                        child.Depth != orgUnit.Depth + 1)
                    {
                        throw new ApplicationServiceException(409, "组织路径数据异常，请刷新后重试");
                    }
                    queue.Enqueue(child);
                }
            }

            if (visitedIds.Contains(newParent.Id))
                throw new ApplicationServiceException(400, "组织节点不能移动到自身下级");
            if (newParent.UnitType == OrgUnitType.Section)
                throw new ApplicationServiceException(400, "课别不能作为上级组织");
            if (source.UnitType <= newParent.UnitType)
                throw new ApplicationServiceException(400, "移动后的节点类型必须是上级组织的下级类型");

            var updatedAt = DateTime.UtcNow;
            foreach (var orgUnit in subtree)
            {
                var parent = orgUnit.Id == source.Id
                    ? newParent
                    : unitsById[orgUnit.ParentId!.Value];
                var newPath = $"{parent.Path}{orgUnit.Id}/";
                if (newPath.Length > 512)
                    throw new ApplicationServiceException(400, "移动后的组织层级过深");

                orgUnit.Path = newPath;
                orgUnit.Depth = parent.Depth + 1;
                orgUnit.UpdatedAt = updatedAt;
            }

            source.ParentId = newParent.Id;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return ToDto(source);
        }
        catch
        {
            await TransactionRollbackHelper.TryRollbackAsync(_unitOfWork);
            throw;
        }
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

    private static void ValidatePath(OrgUnit orgUnit)
    {
        if (string.IsNullOrWhiteSpace(orgUnit.Path) ||
            orgUnit.Path[0] != '/' ||
            orgUnit.Path[^1] != '/')
        {
            throw new ApplicationServiceException(409, "组织路径数据异常，请刷新后重试");
        }

        var segments = orgUnit.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != orgUnit.Depth + 1 ||
            segments.Any(segment => !int.TryParse(segment, out var segmentId) || segmentId <= 0) ||
            !int.TryParse(segments[^1], out var currentId) ||
            currentId != orgUnit.Id)
        {
            throw new ApplicationServiceException(409, "组织路径数据异常，请刷新后重试");
        }
    }

    private static void ValidateLineagePath(
        OrgUnit orgUnit,
        IReadOnlyDictionary<int, OrgUnit> unitsById)
    {
        var visitedIds = new HashSet<int>();
        var current = orgUnit;
        while (true)
        {
            if (!visitedIds.Add(current.Id))
                throw new ApplicationServiceException(409, "组织层级存在循环，请修复后重试");

            ValidatePath(current);
            if (!current.ParentId.HasValue)
            {
                if (current.UnitType != OrgUnitType.Company ||
                    current.Depth != 0 ||
                    current.Path != $"/{current.Id}/")
                {
                    throw new ApplicationServiceException(409, "组织路径数据异常，请刷新后重试");
                }
                return;
            }

            if (!unitsById.TryGetValue(current.ParentId.Value, out var parent) ||
                current.Path != $"{parent.Path}{current.Id}/" ||
                current.Depth != parent.Depth + 1)
            {
                throw new ApplicationServiceException(409, "组织路径数据异常，请刷新后重试");
            }
            current = parent;
        }
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
