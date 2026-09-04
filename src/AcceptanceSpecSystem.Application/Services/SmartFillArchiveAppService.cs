using System.Security.Cryptography;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

public interface ISmartFillArchiveAppService
{
    Task<PagedData<SmartFillArchiveListItemDto>> GetListAsync(
        int userId,
        int companyId,
        bool isAdmin,
        int page,
        int pageSize,
        string? keyword,
        DateTime? from,
        DateTime? to,
        int? orgUnitId,
        string? operatorKeyword,
        CancellationToken cancellationToken = default);

    Task<MatchingDownloadResult> DownloadAsync(
        int userId,
        int companyId,
        bool isAdmin,
        int id,
        CancellationToken cancellationToken = default);
}

public sealed class SmartFillArchiveAppService : ISmartFillArchiveAppService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly IFileStorageService _fileStorage;

    public SmartFillArchiveAppService(
        AppDbContext dbContext,
        IAuthDataScopeService authDataScopeService,
        IFileStorageService fileStorage)
    {
        _dbContext = dbContext;
        _authDataScopeService = authDataScopeService;
        _fileStorage = fileStorage;
    }

    public async Task<PagedData<SmartFillArchiveListItemDto>> GetListAsync(
        int userId,
        int companyId,
        bool isAdmin,
        int page,
        int pageSize,
        string? keyword,
        DateTime? from,
        DateTime? to,
        int? orgUnitId,
        string? operatorKeyword,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, ExecutionHistoryRecordRepository.MaxPageSize);
        var normalizedFrom = NormalizeUtc(from);
        var normalizedTo = NormalizeUtc(to);
        if (normalizedFrom.HasValue && normalizedTo.HasValue && normalizedFrom > normalizedTo)
            throw new ApplicationServiceException(400, "开始时间不能晚于结束时间");

        var records = await CreateAuthorizedQueryAsync(
            userId,
            companyId,
            isAdmin,
            orgUnitId,
            cancellationToken);

        var query =
            from record in records
            join user in _dbContext.SystemUsers.AsNoTracking()
                on record.CreatedByUserId equals (int?)user.Id into users
            from user in users.DefaultIfEmpty()
            join orgUnit in _dbContext.OrgUnits.AsNoTracking()
                on record.OwnerOrgUnitId equals (int?)orgUnit.Id into orgUnits
            from orgUnit in orgUnits.DefaultIfEmpty()
            select new { Record = record, User = user, OrgUnit = orgUnit };

        var normalizedKeyword = keyword?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            query = query.Where(item =>
                item.Record.TaskId.Contains(normalizedKeyword) ||
                item.Record.SourceFileName.Contains(normalizedKeyword));
        }

        var normalizedOperator = operatorKeyword?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedOperator))
        {
            query = query.Where(item =>
                item.User != null &&
                (item.User.Username.Contains(normalizedOperator) ||
                 item.User.Nickname.Contains(normalizedOperator)));
        }

        if (normalizedFrom.HasValue)
            query = query.Where(item => item.Record.CreatedAt >= normalizedFrom.Value);
        if (normalizedTo.HasValue)
            query = query.Where(item => item.Record.CreatedAt <= normalizedTo.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(item => item.Record.CreatedAt)
            .ThenByDescending(item => item.Record.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new SmartFillArchiveListItemDto
            {
                Id = item.Record.Id,
                TaskId = item.Record.TaskId,
                SourceFileName = item.Record.SourceFileName,
                SourceFileType = item.Record.SourceFileType,
                TotalRowCount = item.Record.TotalRowCount,
                AdoptedRowCount = item.Record.AdoptedRowCount,
                SkippedRowCount = item.Record.SkippedRowCount,
                UnmatchedRowCount = item.Record.UnmatchedRowCount,
                OwnerOrgUnitId = item.Record.OwnerOrgUnitId,
                OwnerOrgUnitName = item.OrgUnit == null ? string.Empty : item.OrgUnit.Name,
                CreatedByUserId = item.Record.CreatedByUserId,
                CreatedByDisplayName = item.User == null
                    ? string.Empty
                    : item.User.Nickname != string.Empty
                        ? item.User.Nickname
                        : item.User.Username,
                CreatedAt = item.Record.CreatedAt,
                HasResultArchive = item.Record.ResultArchiveRelativePath != null &&
                                   item.Record.ResultArchiveRelativePath != string.Empty,
                ResultFileName = item.Record.ResultArchiveFileName,
                ResultFileSizeBytes = item.Record.ResultArchiveSizeBytes
            })
            .ToListAsync(cancellationToken);

        return new PagedData<SmartFillArchiveListItemDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<MatchingDownloadResult> DownloadAsync(
        int userId,
        int companyId,
        bool isAdmin,
        int id,
        CancellationToken cancellationToken = default)
    {
        var query = await CreateAuthorizedQueryAsync(
            userId,
            companyId,
            isAdmin,
            orgUnitId: null,
            cancellationToken);
        var record = await query.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ApplicationServiceException(404, "填充存档不存在或无权访问");

        if (string.IsNullOrWhiteSpace(record.ResultArchiveRelativePath) ||
            string.IsNullOrWhiteSpace(record.ResultArchiveFileName) ||
            string.IsNullOrWhiteSpace(record.ResultArchiveContentType) ||
            !record.ResultArchiveSizeBytes.HasValue ||
            string.IsNullOrWhiteSpace(record.ResultArchiveSha256))
            throw new ApplicationServiceException(404, "该历史记录没有可下载的结果文件存档");

        if (!SmartFillResultArchivePathPolicy.IsAllowed(record.ResultArchiveRelativePath))
            throw new ApplicationServiceException(409, "结果存档路径无效，已拒绝读取");

        try
        {
            await using var verificationStream = _fileStorage.OpenReadStream(record.ResultArchiveRelativePath);
            if (verificationStream.Length != record.ResultArchiveSizeBytes.Value)
                throw new ApplicationServiceException(409, "结果存档大小校验失败");

            var actualHash = Convert.ToHexString(
                await SHA256.HashDataAsync(verificationStream, cancellationToken))
                .ToLowerInvariant();
            if (!actualHash.Equals(record.ResultArchiveSha256, StringComparison.OrdinalIgnoreCase))
                throw new ApplicationServiceException(409, "结果存档完整性校验失败");

            return new MatchingDownloadResult(
                _fileStorage.OpenReadStream(record.ResultArchiveRelativePath),
                record.ResultArchiveContentType,
                Path.GetFileName(record.ResultArchiveFileName));
        }
        catch (FileNotFoundException)
        {
            throw new ApplicationServiceException(404, "结果存档文件已不存在");
        }
        catch (DirectoryNotFoundException)
        {
            throw new ApplicationServiceException(404, "结果存档文件已不存在");
        }
    }

    private async Task<IQueryable<ExecutionHistoryRecord>> CreateAuthorizedQueryAsync(
        int userId,
        int companyId,
        bool isAdmin,
        int? orgUnitId,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.ExecutionHistoryRecords
            .AsNoTracking()
            .Where(record =>
                record.CompanyId == companyId &&
                record.TaskType == ExecutionHistoryTaskTypes.SmartFill);

        if (isAdmin)
        {
            if (!orgUnitId.HasValue)
                return query;

            var validOrgUnit = await _dbContext.OrgUnits
                .AsNoTracking()
                .AnyAsync(org =>
                    org.Id == orgUnitId.Value &&
                    org.CompanyId == companyId &&
                    org.IsActive,
                    cancellationToken);
            if (!validOrgUnit)
                throw new ApplicationServiceException(400, "所选部门不存在、已停用或不属于当前公司");

            return query.Where(record => record.OwnerOrgUnitId == orgUnitId.Value);
        }

        var scope = await _authDataScopeService.GetScopeAsync(
            userId,
            companyId,
            "spec",
            cancellationToken)
            ?? throw new ApplicationServiceException(403, "当前账号没有可用的部门数据范围");
        if (!scope.OrgUnitId.HasValue)
            return query.Where(record =>
                !record.OwnerOrgUnitId.HasValue && record.CreatedByUserId == userId);

        if (orgUnitId.HasValue && orgUnitId.Value != scope.OrgUnitId.Value)
            throw new ApplicationServiceException(403, "普通用户不能查看其他部门的填充存档");

        var currentOrgUnitId = scope.OrgUnitId.Value;
        return query.Where(record =>
            record.OwnerOrgUnitId == currentOrgUnitId ||
            (!record.OwnerOrgUnitId.HasValue && record.CreatedByUserId == userId));
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue)
            return null;

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }
}
