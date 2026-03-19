using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 审计日志仓储接口
/// </summary>
public interface IAuditLogRepository : IRepository<AuditLog>
{
    /// <summary>
    /// 分页查询审计日志
    /// </summary>
    Task<(IReadOnlyList<AuditLog> Items, int Total)> GetPagedAsync(
        int page,
        int pageSize,
        AuditLogSource? source = null,
        AuditLogLevel? level = null,
        string? username = null,
        string? requestMethod = null,
        string? keyword = null,
        DateTime? from = null,
        DateTime? to = null,
        int? minStatusCode = null,
        int? maxStatusCode = null);

    /// <summary>
    /// 删除指定时间点之前的审计日志
    /// </summary>
    Task<int> DeleteBeforeAsync(DateTime beforeTime);

    /// <summary>
    /// 按时间范围删除审计日志
    /// </summary>
    Task<int> DeleteByRangeAsync(DateTime? from = null, DateTime? to = null);
}
