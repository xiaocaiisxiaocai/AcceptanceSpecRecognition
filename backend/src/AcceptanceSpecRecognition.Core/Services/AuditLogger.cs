using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Core.Services;

/// <summary>
/// 审计日志器实现 - 使用文件存储，避免内存泄漏
/// </summary>
public class AuditLogger : IAuditLogger
{
    private readonly IJsonStorageService _storage;
    private readonly IConfigManager _configManager;
    private readonly string _logPath = "./data/audit_log.json";
    private const int MaxEntriesInMemory = 1000; // 内存中最多保留的条目数

    public AuditLogger(IJsonStorageService storage, IConfigManager configManager)
    {
        _storage = storage;
        _configManager = configManager;
    }

    public async Task LogQueryAsync(QueryLogEntry entry)
    {
        var logEntry = new AuditLogEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            ActionType = "query",
            Timestamp = DateTime.UtcNow,
            Details = $"[{entry.MatchMode}] 查询: {entry.QueryText}, 结果数: {entry.ResultCount}, 最高分: {entry.TopScore:F3}, 置信度: {entry.Confidence}, 耗时: {entry.DurationMs}ms"
        };

        await AppendEntryAsync(logEntry);
    }

    public async Task LogUserActionAsync(UserActionLogEntry entry)
    {
        var logEntry = new AuditLogEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            ActionType = entry.Action,
            Timestamp = DateTime.UtcNow,
            RecordId = entry.RecordId,
            Details = entry.Details
        };

        await AppendEntryAsync(logEntry);
    }

    public async Task LogConfigChangeAsync(ConfigChangeLogEntry entry)
    {
        var logEntry = new AuditLogEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            ActionType = "config_change",
            Timestamp = DateTime.UtcNow,
            Details = $"配置区域: {entry.ConfigSection}, 变更: {entry.Changes}"
        };

        await AppendEntryAsync(logEntry);
    }

    /// <summary>
    /// 追加日志条目，并在超出限制时自动清理旧条目
    /// </summary>
    private async Task AppendEntryAsync(AuditLogEntry entry)
    {
        var store = await _storage.ReadAsync<AuditLogStore>(_logPath) ?? new AuditLogStore();
        store.Entries ??= new List<AuditLogEntry>();

        store.Entries.Add(entry);

        // 从配置获取最大条目数
        var config = _configManager.GetAll();
        var maxEntriesInFile = config.Batch.MaxAuditEntries;

        // 如果超出最大条目数，删除最旧的条目
        if (store.Entries.Count > maxEntriesInFile)
        {
            var excessCount = store.Entries.Count - maxEntriesInFile;
            store.Entries = store.Entries
                .OrderByDescending(e => e.Timestamp)
                .Take(maxEntriesInFile)
                .ToList();
        }

        store.UpdatedAt = DateTime.UtcNow;
        await _storage.WriteAsync(_logPath, store);
    }

    public async Task<AuditQueryResult> QueryLogsAsync(AuditLogFilter filter)
    {
        var store = await _storage.ReadAsync<AuditLogStore>(_logPath);
        var entries = store?.Entries ?? new List<AuditLogEntry>();

        var query = entries.AsEnumerable();

        if (filter.StartTime.HasValue)
        {
            query = query.Where(e => e.Timestamp >= filter.StartTime.Value);
        }

        if (filter.EndTime.HasValue)
        {
            query = query.Where(e => e.Timestamp <= filter.EndTime.Value);
        }

        if (!string.IsNullOrEmpty(filter.ActionType))
        {
            query = query.Where(e => e.ActionType == filter.ActionType);
        }

        var filtered = query.OrderByDescending(e => e.Timestamp).ToList();
        var totalCount = filtered.Count;

        var paged = filtered
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToList();

        return new AuditQueryResult
        {
            Entries = paged,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    /// <summary>
    /// 清除所有审计日志
    /// </summary>
    public async Task ClearLogsAsync()
    {
        var emptyStore = new AuditLogStore
        {
            Entries = new List<AuditLogEntry>(),
            UpdatedAt = DateTime.UtcNow
        };
        await _storage.WriteAsync(_logPath, emptyStore);
    }
}
