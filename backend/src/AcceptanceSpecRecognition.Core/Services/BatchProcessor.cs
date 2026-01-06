using System.Collections.Concurrent;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecRecognition.Core.Services;

/// <summary>
/// 批量处理器实现 - 支持并行处理
/// </summary>
public class BatchProcessor : IBatchProcessor
{
    private readonly IMatchingEngine _matchingEngine;
    private readonly IConfigManager _configManager;
    private readonly ILogger<BatchProcessor> _logger;
    private readonly ConcurrentDictionary<string, BatchTask> _tasks = new();
    private readonly Timer _cleanupTimer;

    public BatchProcessor(IMatchingEngine matchingEngine, IConfigManager configManager, ILogger<BatchProcessor> logger)
    {
        _matchingEngine = matchingEngine;
        _configManager = configManager;
        _logger = logger;

        // 启动定期清理过期任务的定时器
        var cleanupInterval = TimeSpan.FromMinutes(5);
        _cleanupTimer = new Timer(CleanupExpiredTasks, null, cleanupInterval, cleanupInterval);
    }

    /// <summary>
    /// 清理过期的已完成任务
    /// </summary>
    private void CleanupExpiredTasks(object? state)
    {
        var config = _configManager.GetAll();
        var taskRetentionTime = TimeSpan.FromMinutes(config.Batch.TaskRetentionMinutes);

        var expiredTaskIds = _tasks
            .Where(t => t.Value.Progress.CompletedAt.HasValue &&
                       DateTime.UtcNow - t.Value.Progress.CompletedAt.Value > taskRetentionTime)
            .Select(t => t.Key)
            .ToList();

        foreach (var taskId in expiredTaskIds)
        {
            if (_tasks.TryRemove(taskId, out var task))
            {
                task.CancellationTokenSource.Dispose();
                _logger.LogDebug("清理过期任务: {TaskId}", taskId);
            }
        }

        if (expiredTaskIds.Count > 0)
        {
            _logger.LogInformation("已清理 {Count} 个过期任务，当前任务数: {Current}",
                expiredTaskIds.Count, _tasks.Count);
        }
    }

    public async Task<string> StartBatchAsync(List<MatchQuery> queries)
    {
        var taskId = Guid.NewGuid().ToString("N");
        var task = new BatchTask
        {
            Progress = new BatchProgress
            {
                TaskId = taskId,
                Total = queries.Count,
                Completed = 0,
                Failed = 0,
                Status = "running",
                StartedAt = DateTime.UtcNow
            },
            CancellationTokenSource = new CancellationTokenSource()
        };

        _tasks[taskId] = task;

        // 在后台执行批量处理
        _ = ProcessBatchInternalAsync(taskId, queries, task.CancellationTokenSource.Token);

        return taskId;
    }

    public async Task<BatchResult> ProcessBatchAsync(BatchRequest request)
    {
        var taskId = await StartBatchAsync(request.Queries);

        // 等待处理完成
        while (true)
        {
            var progress = GetProgress(taskId);
            if (progress?.Status == "completed" || progress?.Status == "failed" || progress?.Status == "cancelled")
            {
                break;
            }
            await Task.Delay(100);
        }

        var result = await GetResultAsync(taskId);
        return result ?? new BatchResult { TaskId = taskId };
    }

    private async Task ProcessBatchInternalAsync(string taskId, List<MatchQuery> queries, CancellationToken cancellationToken)
    {
        var task = _tasks[taskId];
        var results = new ConcurrentBag<(int Index, MatchResult Result)>();
        var failedCount = 0;

        // 从配置获取最大并发数
        var config = _configManager.GetAll();
        var maxConcurrency = config.Batch.MaxConcurrency;

        try
        {
            // 使用SemaphoreSlim控制并发数
            using var semaphore = new SemaphoreSlim(maxConcurrency);

            var processingTasks = queries.Select(async (query, index) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var result = await _matchingEngine.MatchAsync(query);
                    results.Add((index, result));
                    lock (task.Progress)
                    {
                        task.Progress.Completed++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "批量处理第{Index}条查询失败: Project={Project}, TechnicalSpec={TechnicalSpec}",
                        index, query.Project, query.TechnicalSpec);
                    Interlocked.Increment(ref failedCount);
                    lock (task.Progress)
                    {
                        task.Progress.Failed++;
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(processingTasks);

            if (cancellationToken.IsCancellationRequested)
            {
                task.Progress.Status = "cancelled";
            }
            else
            {
                task.Progress.Status = "completed";
            }
        }
        catch (OperationCanceledException)
        {
            task.Progress.Status = "cancelled";
            _logger.LogWarning("批量处理任务{TaskId}被取消", taskId);
        }
        catch (Exception ex)
        {
            task.Progress.Status = "failed";
            _logger.LogError(ex, "批量处理任务{TaskId}失败", taskId);
        }
        finally
        {
            task.Progress.CompletedAt = DateTime.UtcNow;
            // 按原始顺序排序结果
            var orderedResults = results.OrderBy(r => r.Index).Select(r => r.Result).ToList();
            task.Result = new BatchResult
            {
                TaskId = taskId,
                Results = orderedResults,
                Summary = GenerateSummary(orderedResults)
            };
        }
    }

    public BatchProgress? GetProgress(string taskId)
    {
        if (_tasks.TryGetValue(taskId, out var task))
        {
            return task.Progress;
        }
        return null;
    }

    public async Task CancelAsync(string taskId)
    {
        if (_tasks.TryGetValue(taskId, out var task))
        {
            task.CancellationTokenSource.Cancel();
            task.Progress.Status = "cancelled";
        }
        await Task.CompletedTask;
    }

    public bool CancelTask(string taskId)
    {
        if (_tasks.TryGetValue(taskId, out var task))
        {
            if (task.Progress.Status == "running")
            {
                task.CancellationTokenSource.Cancel();
                task.Progress.Status = "cancelled";
                return true;
            }
        }
        return false;
    }

    public async Task<BatchResult?> GetResultAsync(string taskId)
    {
        if (_tasks.TryGetValue(taskId, out var task))
        {
            return task.Result;
        }
        return null;
    }

    private BatchSummary GenerateSummary(List<MatchResult> results)
    {
        return new BatchSummary
        {
            TotalCount = results.Count,
            SuccessCount = results.Count(r => r.Confidence == ConfidenceLevel.Success),
            LowConfidenceCount = results.Count(r => r.Confidence == ConfidenceLevel.Low)
        };
    }

    private class BatchTask
    {
        public BatchProgress Progress { get; set; } = new();
        public BatchResult? Result { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; } = new();
    }
}
