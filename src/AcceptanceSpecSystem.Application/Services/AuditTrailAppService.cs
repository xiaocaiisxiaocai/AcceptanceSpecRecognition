using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Application.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Application.Services;

public interface IAuditTrailAppService
{
    Task WriteAsync(AuditTrailWriteCommand command, CancellationToken cancellationToken = default);

    Task<PagedResult<AuditLogListItemDto>> GetPagedAsync(
        int page,
        int pageSize,
        AuditLogSource? source,
        AuditLogLevel? level,
        string? username,
        string? requestMethod,
        string? keyword,
        DateTime? from,
        DateTime? to,
        int? minStatusCode,
        int? maxStatusCode,
        CancellationToken cancellationToken = default);

    Task<AuditLogDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<int> DeleteByRangeAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
}
public sealed class AuditTrailAppService : IAuditTrailAppService
{
    private readonly IUnitOfWork _unitOfWork;

    public AuditTrailAppService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task WriteAsync(AuditTrailWriteCommand command, CancellationToken cancellationToken = default)
    {
        var entity = new AuditLog
        {
            Source = command.Source,
            Level = command.Level,
            EventType = command.EventType,
            Username = TrimToLength(command.Username, 64),
            RequestMethod = command.RequestMethod,
            RequestPath = command.RequestPath,
            QueryString = TrimToLength(command.QueryString, 1024),
            StatusCode = command.StatusCode,
            DurationMs = command.DurationMs,
            ClientIp = command.ClientIp,
            UserAgent = TrimToLength(command.UserAgent, 512),
            ClientTraceId = TrimToLength(command.ClientTraceId, 64),
            ClientId = TrimToLength(command.ClientId, 64),
            FrontendRoute = TrimToLength(command.FrontendRoute, 512),
            Details = TrimToLength(command.Details, 4000),
            CreatedAt = command.CreatedAt
        };

        await _unitOfWork.AuditLogs.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<AuditLogListItemDto>> GetPagedAsync(
        int page,
        int pageSize,
        AuditLogSource? source,
        AuditLogLevel? level,
        string? username,
        string? requestMethod,
        string? keyword,
        DateTime? from,
        DateTime? to,
        int? minStatusCode,
        int? maxStatusCode,
        CancellationToken cancellationToken = default)
    {
        var (items, total) = await _unitOfWork.AuditLogs.GetPagedAsync(
            page, pageSize, source, level, username, requestMethod, keyword, from, to,
            minStatusCode, maxStatusCode, cancellationToken);

        return new PagedResult<AuditLogListItemDto>
        {
            Items = items.Select(ToListDto).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<AuditLogDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.AuditLogs.GetByIdAsync(id, cancellationToken);
        return entity == null ? null : ToDetailDto(entity);
    }

    public Task<int> DeleteByRangeAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
    {
        if (!from.HasValue && !to.HasValue)
            throw new ApplicationServiceException(400, "请至少提供 from 或 to");
        if (from.HasValue && to.HasValue && from > to)
            throw new ApplicationServiceException(400, "from 不能晚于 to");

        return _unitOfWork.AuditLogs.DeleteByRangeAsync(from, to, cancellationToken);
    }

    private static AuditLogListItemDto ToListDto(AuditLog entity) => new()
    {
        Id = entity.Id,
        Source = entity.Source,
        Level = entity.Level,
        EventType = entity.EventType,
        Username = entity.Username,
        RequestMethod = entity.RequestMethod,
        RequestPath = entity.RequestPath,
        QueryString = entity.QueryString,
        StatusCode = entity.StatusCode,
        DurationMs = entity.DurationMs,
        ClientIp = entity.ClientIp,
        UserAgent = entity.UserAgent,
        ClientTraceId = entity.ClientTraceId,
        ClientId = entity.ClientId,
        FrontendRoute = entity.FrontendRoute,
        CreatedAt = entity.CreatedAt
    };

    private static AuditLogDetailDto ToDetailDto(AuditLog entity)
    {
        var dto = new AuditLogDetailDto
        {
            Details = entity.Details
        };
        var summary = ToListDto(entity);
        dto.Id = summary.Id;
        dto.Source = summary.Source;
        dto.Level = summary.Level;
        dto.EventType = summary.EventType;
        dto.Username = summary.Username;
        dto.RequestMethod = summary.RequestMethod;
        dto.RequestPath = summary.RequestPath;
        dto.QueryString = summary.QueryString;
        dto.StatusCode = summary.StatusCode;
        dto.DurationMs = summary.DurationMs;
        dto.ClientIp = summary.ClientIp;
        dto.UserAgent = summary.UserAgent;
        dto.ClientTraceId = summary.ClientTraceId;
        dto.ClientId = summary.ClientId;
        dto.FrontendRoute = summary.FrontendRoute;
        dto.CreatedAt = summary.CreatedAt;
        return dto;
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
