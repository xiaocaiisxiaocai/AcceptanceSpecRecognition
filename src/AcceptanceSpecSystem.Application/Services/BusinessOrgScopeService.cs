using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

public interface IBusinessOrgScopeService
{
    Task<DataScopeResult> ResolveUploadScopeAsync(
        DataScopeResult callerScope,
        bool isAdmin,
        int? requestedOrgUnitId,
        CancellationToken cancellationToken = default);

    Task<DataScopeResult> ResolveFileScopeAsync(
        DataScopeResult callerScope,
        WordFile file,
        CancellationToken cancellationToken = default);

    Task<DataScopeResult> ResolveCurrentScopeAsync(
        DataScopeResult callerScope,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    Task<SpecAccessContext> ResolveFileScopeAsync(
        SpecAccessContext callerScope,
        WordFile file,
        CancellationToken cancellationToken = default);

    Task<BusinessOrgContextDto> GetContextAsync(
        DataScopeResult callerScope,
        bool isAdmin,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 将管理员的公司级访问权收窄为单次业务任务的唯一组织归属。
/// </summary>
public sealed class BusinessOrgScopeService : IBusinessOrgScopeService
{
    private readonly AppDbContext _dbContext;

    public BusinessOrgScopeService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DataScopeResult> ResolveUploadScopeAsync(
        DataScopeResult callerScope,
        bool isAdmin,
        int? requestedOrgUnitId,
        CancellationToken cancellationToken = default)
    {
        var targetOrgUnitId = callerScope.OrgUnitId;
        if (isAdmin)
        {
            var hasBusinessOrgUnits = await _dbContext.OrgUnits
                .AsNoTracking()
                .AnyAsync(org =>
                    org.CompanyId == callerScope.CompanyId &&
                    org.IsActive &&
                    org.UnitType != OrgUnitType.Company,
                    cancellationToken);

            if (hasBusinessOrgUnits && !requestedOrgUnitId.HasValue)
            {
                throw new ApplicationServiceException(400, "请选择本次导入或智能填充的业务归属部门");
            }

            targetOrgUnitId = requestedOrgUnitId ?? callerScope.OrgUnitId;
        }
        else if (requestedOrgUnitId.HasValue &&
                 requestedOrgUnitId.Value != callerScope.OrgUnitId)
        {
            throw new ApplicationServiceException(403, "普通用户只能使用本人所属部门");
        }

        if (!targetOrgUnitId.HasValue)
        {
            throw new ApplicationServiceException(400, "当前账号没有有效的组织归属");
        }

        var target = await GetActiveOrgUnitAsync(
            callerScope.CompanyId,
            targetOrgUnitId.Value,
            cancellationToken);
        if (target == null)
        {
            throw new ApplicationServiceException(400, "所选业务归属不存在、已停用或不属于当前公司");
        }

        if (isAdmin &&
            requestedOrgUnitId.HasValue &&
            target.UnitType == OrgUnitType.Company)
        {
            throw new ApplicationServiceException(400, "存在下级组织时不能将业务任务归属到公司根组织");
        }

        return CreateExactScope(callerScope.UserId, callerScope.CompanyId, target.Id);
    }

    public async Task<DataScopeResult> ResolveFileScopeAsync(
        DataScopeResult callerScope,
        WordFile file,
        CancellationToken cancellationToken = default)
    {
        var target = await ValidateFileOrgAsync(
            callerScope.CompanyId,
            file,
            cancellationToken);
        return CreateExactScope(callerScope.UserId, callerScope.CompanyId, target.Id);
    }

    public async Task<DataScopeResult> ResolveCurrentScopeAsync(
        DataScopeResult callerScope,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        if (isAdmin)
        {
            var hasBusinessOrgUnits = await _dbContext.OrgUnits
                .AsNoTracking()
                .AnyAsync(org =>
                    org.CompanyId == callerScope.CompanyId &&
                    org.IsActive &&
                    org.UnitType != OrgUnitType.Company,
                    cancellationToken);
            if (hasBusinessOrgUnits)
            {
                throw new ApplicationServiceException(400, "当前操作缺少源文件的业务归属，请重新上传文件");
            }
        }

        if (!callerScope.OrgUnitId.HasValue)
        {
            throw new ApplicationServiceException(400, "当前账号没有有效的组织归属");
        }

        var target = await GetActiveOrgUnitAsync(
            callerScope.CompanyId,
            callerScope.OrgUnitId.Value,
            cancellationToken)
            ?? throw new ApplicationServiceException(400, "当前账号的组织归属不存在或已停用");
        return CreateExactScope(callerScope.UserId, callerScope.CompanyId, target.Id);
    }

    public async Task<SpecAccessContext> ResolveFileScopeAsync(
        SpecAccessContext callerScope,
        WordFile file,
        CancellationToken cancellationToken = default)
    {
        var target = await ValidateFileOrgAsync(
            callerScope.CompanyId,
            file,
            cancellationToken);
        return CreateExactAccessContext(callerScope.UserId, callerScope.CompanyId, target.Id);
    }

    public async Task<BusinessOrgContextDto> GetContextAsync(
        DataScopeResult callerScope,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var activeOrgUnits = await _dbContext.OrgUnits
            .AsNoTracking()
            .Where(org => org.CompanyId == callerScope.CompanyId && org.IsActive)
            .OrderBy(org => org.Depth)
            .ThenBy(org => org.Sort)
            .ThenBy(org => org.Id)
            .Select(org => new BusinessOrgOptionDto
            {
                Id = org.Id,
                Name = org.Name,
                UnitType = org.UnitType,
                Path = org.Path,
                Depth = org.Depth
            })
            .ToListAsync(cancellationToken);

        if (!isAdmin)
        {
            var current = activeOrgUnits.FirstOrDefault(org => org.Id == callerScope.OrgUnitId)
                ?? throw new ApplicationServiceException(400, "当前账号没有有效的组织归属");
            return new BusinessOrgContextDto
            {
                RequiresSelection = false,
                CurrentOrgUnitId = current.Id,
                CurrentOrgUnitName = current.Name,
                Options = [current]
            };
        }

        var businessOptions = activeOrgUnits
            .Where(org => org.UnitType != OrgUnitType.Company)
            .ToList();
        if (businessOptions.Count > 0)
        {
            return new BusinessOrgContextDto
            {
                RequiresSelection = true,
                Options = businessOptions
            };
        }

        var root = activeOrgUnits.FirstOrDefault(org =>
            org.Id == callerScope.OrgUnitId && org.UnitType == OrgUnitType.Company)
            ?? activeOrgUnits.FirstOrDefault(org => org.UnitType == OrgUnitType.Company)
            ?? throw new ApplicationServiceException(400, "当前公司没有有效的根组织");
        return new BusinessOrgContextDto
        {
            RequiresSelection = false,
            CurrentOrgUnitId = root.Id,
            CurrentOrgUnitName = root.Name,
            IsCompanyFallback = true,
            Options = [root]
        };
    }

    private async Task<OrgUnit> ValidateFileOrgAsync(
        int companyId,
        WordFile file,
        CancellationToken cancellationToken)
    {
        if (file.CompanyId.HasValue && file.CompanyId.Value != companyId)
        {
            throw new ApplicationServiceException(403, "源文件不属于当前公司");
        }

        if (!file.OwnerOrgUnitId.HasValue)
        {
            throw new ApplicationServiceException(400, "源文件缺少业务归属，请重新上传");
        }

        return await GetActiveOrgUnitAsync(companyId, file.OwnerOrgUnitId.Value, cancellationToken)
            ?? throw new ApplicationServiceException(400, "源文件的业务归属不存在、已停用或不属于当前公司");
    }

    private Task<OrgUnit?> GetActiveOrgUnitAsync(
        int companyId,
        int orgUnitId,
        CancellationToken cancellationToken)
    {
        return _dbContext.OrgUnits
            .AsNoTracking()
            .FirstOrDefaultAsync(org =>
                org.Id == orgUnitId &&
                org.CompanyId == companyId &&
                org.IsActive,
                cancellationToken);
    }

    private static DataScopeResult CreateExactScope(int userId, int companyId, int orgUnitId) =>
        new()
        {
            UserId = userId,
            CompanyId = companyId,
            OrgUnitId = orgUnitId,
            IsAll = false,
            IncludeSelf = false,
            OrgUnitIds = [orgUnitId]
        };

    private static SpecAccessContext CreateExactAccessContext(
        int userId,
        int companyId,
        int orgUnitId) =>
        new()
        {
            UserId = userId,
            CompanyId = companyId,
            OrgUnitId = orgUnitId,
            IsAll = false,
            IncludeSelf = false,
            OrgUnitIds = [orgUnitId]
        };
}
