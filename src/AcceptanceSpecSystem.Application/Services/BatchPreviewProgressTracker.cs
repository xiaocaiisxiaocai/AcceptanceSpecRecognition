using Microsoft.Extensions.Caching.Memory;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 批量预览进度跟踪器。
/// </summary>
public sealed class BatchPreviewProgressTracker
{
    private static readonly TimeSpan EntrySlidingExpiration = TimeSpan.FromMinutes(20);

    private readonly IMemoryCache _memoryCache;
    private readonly object _startSyncRoot = new();

    public BatchPreviewProgressTracker(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    public bool TryStart(MatchingUserContext owner, string? requestId, int totalTables)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return true;
        }

        var key = BuildKey(owner, requestId);
        var now = DateTime.UtcNow;
        var entry = new ProgressEntry
        {
            RequestId = requestId.Trim(),
            Status = "running",
            Stage = "preparing",
            StageText = "正在准备匹配任务",
            DetailText = $"已接收 {totalTables} 个表格的预览请求",
            ProgressPercent = 2,
            StartedAt = now,
            UpdatedAt = now
        };

        lock (_startSyncRoot)
        {
            if (_memoryCache.TryGetValue<ProgressEntry>(key, out var existing) &&
                existing?.Status == "running")
            {
                return false;
            }

            SetEntry(key, entry);
            return true;
        }
    }

    public void Update(
        MatchingUserContext owner,
        string? requestId,
        string stage,
        string stageText,
        string? detailText = null,
        int? completedItems = null,
        int? totalItems = null,
        double? progressPercent = null,
        string? status = null)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        var key = BuildKey(owner, requestId);
        var entry = GetOrCreateEntry(key, requestId.Trim());
        lock (entry.SyncRoot)
        {
            // completed/failed 属于终态，不允许被后续异步进度回调回写成运行中阶段。
            if ((entry.Status == "completed" || entry.Status == "failed") &&
                string.IsNullOrWhiteSpace(status))
            {
                return;
            }

            entry.Status = string.IsNullOrWhiteSpace(status) ? entry.Status : status;
            entry.Stage = string.IsNullOrWhiteSpace(stage) ? entry.Stage : stage;
            entry.StageText = string.IsNullOrWhiteSpace(stageText) ? entry.StageText : stageText;
            entry.DetailText = detailText;

            if (completedItems.HasValue)
            {
                entry.CompletedItems = Math.Max(0, completedItems.Value);
            }

            if (totalItems.HasValue)
            {
                entry.TotalItems = Math.Max(0, totalItems.Value);
            }

            entry.ProgressPercent = NormalizePercent(Math.Max(progressPercent ?? entry.ProgressPercent, entry.ProgressPercent));
            entry.UpdatedAt = DateTime.UtcNow;
        }

        SetEntry(key, entry);
    }

    public void Complete(
        MatchingUserContext owner,
        string? requestId,
        int completedItems,
        int totalItems,
        string? detailText = null)
    {
        Update(
            owner,
            requestId,
            stage: "completed",
            stageText: "匹配预览已完成",
            detailText: detailText ?? $"已完成 {completedItems}/{Math.Max(totalItems, completedItems)} 行",
            completedItems: completedItems,
            totalItems: totalItems,
            progressPercent: 100,
            status: "completed");
    }

    public void Fail(MatchingUserContext owner, string? requestId, string? message)
    {
        Update(
            owner,
            requestId,
            stage: "failed",
            stageText: "匹配预览失败",
            // 进度缓存可能被轮询接口直接返回，禁止写入原始异常或外部服务响应。
            detailText: "匹配预览失败，请稍后重试",
            progressPercent: 100,
            status: "failed");
    }

    public BatchPreviewProgressResponse? GetSnapshot(MatchingUserContext owner, string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return null;
        }

        var key = BuildKey(owner, requestId);
        if (!_memoryCache.TryGetValue<ProgressEntry>(key, out var entry) || entry == null)
        {
            return null;
        }

        lock (entry.SyncRoot)
        {
            var now = DateTime.UtcNow;
            return new BatchPreviewProgressResponse
            {
                RequestId = entry.RequestId,
                Status = entry.Status,
                Stage = entry.Stage,
                StageText = entry.StageText,
                DetailText = entry.DetailText,
                CompletedItems = entry.CompletedItems,
                TotalItems = entry.TotalItems,
                ProgressPercent = NormalizePercent(entry.ProgressPercent),
                StartedAt = entry.StartedAt,
                UpdatedAt = entry.UpdatedAt,
                ElapsedMs = Math.Max(0, (long)(now - entry.StartedAt).TotalMilliseconds)
            };
        }
    }

    private ProgressEntry GetOrCreateEntry(ProgressCacheKey key, string requestId)
    {
        return _memoryCache.GetOrCreate(
            key,
            cacheEntry =>
            {
                cacheEntry.SetSlidingExpiration(EntrySlidingExpiration);
                var now = DateTime.UtcNow;
                return new ProgressEntry
                {
                    RequestId = requestId,
                    Status = "running",
                    Stage = "preparing",
                    StageText = "正在准备匹配任务",
                    ProgressPercent = 0,
                    StartedAt = now,
                    UpdatedAt = now
                };
            })!;
    }

    private void SetEntry(ProgressCacheKey key, ProgressEntry entry)
    {
        _memoryCache.Set(
            key,
            entry,
            new MemoryCacheEntryOptions
            {
                SlidingExpiration = EntrySlidingExpiration
            });
    }

    private static double NormalizePercent(double percent)
    {
        return Math.Clamp(Math.Round(percent, 1), 0, 100);
    }

    private static ProgressCacheKey BuildKey(MatchingUserContext owner, string requestId)
    {
        return new ProgressCacheKey(owner.CompanyId, owner.UserId, requestId.Trim());
    }

    private sealed record ProgressCacheKey(int CompanyId, int UserId, string RequestId);

    private sealed class ProgressEntry
    {
        public object SyncRoot { get; } = new();

        public string RequestId { get; set; } = string.Empty;

        public string Status { get; set; } = "running";

        public string Stage { get; set; } = "preparing";

        public string StageText { get; set; } = string.Empty;

        public string? DetailText { get; set; }

        public int CompletedItems { get; set; }

        public int TotalItems { get; set; }

        public double ProgressPercent { get; set; }

        public DateTime StartedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
