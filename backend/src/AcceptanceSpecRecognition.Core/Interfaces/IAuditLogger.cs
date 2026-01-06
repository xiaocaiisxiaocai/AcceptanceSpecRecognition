using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Core.Interfaces;

/// <summary>
/// 审计日志器接口
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// 记录查询
    /// </summary>
    Task LogQueryAsync(QueryLogEntry entry);

    /// <summary>
    /// 记录用户操作
    /// </summary>
    Task LogUserActionAsync(UserActionLogEntry entry);

    /// <summary>
    /// 记录配置修改
    /// </summary>
    Task LogConfigChangeAsync(ConfigChangeLogEntry entry);

    /// <summary>
    /// 查询日志
    /// </summary>
    Task<AuditQueryResult> QueryLogsAsync(AuditLogFilter filter);

    /// <summary>
    /// 清除所有审计日志
    /// </summary>
    Task ClearLogsAsync();
}
