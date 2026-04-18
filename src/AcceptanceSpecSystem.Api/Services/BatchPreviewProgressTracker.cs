using AcceptanceSpecSystem.Api.DTOs;
using Microsoft.Extensions.Caching.Memory;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 批量预览进度跟踪器。
/// </summary>
public sealed class BatchPreviewProgressTracker
{
    private static readonly TimeSpan EntrySlidingExpiration = TimeSpan.FromMinutes(20);

    private readonly IMemoryCache _memoryCache;

    public BatchPreviewProgressTracker(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    public void Start(string? requestId, int totalTables)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

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

        SetEntry(entry);
    }

    public void Update(
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

        var entry = GetOrCreateEntry(requestId.Trim());
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

        SetEntry(entry);
    }

    public void Complete(string? requestId, int completedItems, int totalItems, string? detailText = null)
    {
        Update(
            requestId,
            stage: "completed",
            stageText: "匹配预览已完成",
            detailText: detailText ?? $"已完成 {completedItems}/{Math.Max(totalItems, completedItems)} 行",
            completedItems: completedItems,
            totalItems: totalItems,
            progressPercent: 100,
            status: "completed");
    }

    public void Fail(string? requestId, string? message)
    {
        Update(
            requestId,
            stage: "failed",
            stageText: "匹配预览失败",
            detailText: string.IsNullOrWhiteSpace(message) ? "请稍后重试" : message,
            progressPercent: 100,
            status: "failed");
    }

    public BatchPreviewProgressResponse? GetSnapshot(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return null;
        }

        if (!_memoryCache.TryGetValue<ProgressEntry>(requestId.Trim(), out var entry) || entry == null)
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

    private ProgressEntry GetOrCreateEntry(string requestId)
    {
        return _memoryCache.GetOrCreate(
            requestId,
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

    private void SetEntry(ProgressEntry entry)
    {
        _memoryCache.Set(
            entry.RequestId,
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
