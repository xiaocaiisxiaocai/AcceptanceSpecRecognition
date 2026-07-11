using AcceptanceSpecSystem.Application.Options;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 向量缓存后台预热服务
/// </summary>
public sealed class EmbeddingCacheWarmupService : BackgroundService
{
    private readonly EmbeddingCacheWarmupManager _manager;
    private readonly IEmbeddingCacheWarmupTrigger _trigger;
    private readonly ILogger<EmbeddingCacheWarmupService> _logger;

    public EmbeddingCacheWarmupService(
        EmbeddingCacheWarmupManager manager,
        IEmbeddingCacheWarmupTrigger trigger,
        ILogger<EmbeddingCacheWarmupService> logger)
    {
        _manager = manager;
        _trigger = trigger;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startupHandled = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _manager.GetOptions();
            if (!startupHandled)
            {
                startupHandled = true;
                if (options.Enabled && options.RunOnStartup)
                {
                    await WarmupOnceAsync(requireEnabled: true, stoppingToken);
                    continue;
                }
            }

            var delay = options.Enabled
                ? GetNextDelay(options)
                : GetIntervalDelay(options);
            var triggered = await WaitForTriggerOrDelayAsync(delay, stoppingToken);
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            // 导入触发保持原有语义，不受定时开关限制；定时触发仍遵循 Enabled。
            await WarmupOnceAsync(requireEnabled: !triggered, stoppingToken);
        }
    }

    private async Task WarmupOnceAsync(bool requireEnabled, CancellationToken stoppingToken)
    {
        if ((requireEnabled && !_manager.GetOptions().Enabled) || stoppingToken.IsCancellationRequested)
            return;

        var result = await _manager.RunOnceAsync(stoppingToken);
        if (!result.Started)
            _logger.LogDebug("向量缓存预热已有任务执行中，跳过本次自动预热");
    }

    private static TimeSpan GetNextDelay(EmbeddingCacheWarmupOptions options)
    {
        if (TryGetRunAtLocalTime(options.RunAtLocalTime, out var localTime))
        {
            var now = TimeProvider.System.GetLocalNow().DateTime;
            var next = now.Date.Add(localTime);
            if (next <= now)
            {
                next = next.AddDays(1);
            }

            return next - now;
        }

        return GetIntervalDelay(options);
    }

    private static TimeSpan GetIntervalDelay(EmbeddingCacheWarmupOptions options)
        => TimeSpan.FromHours(Math.Max(1, options.IntervalHours));

    private async Task<bool> WaitForTriggerOrDelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var triggerTask = _trigger.WaitAsync(waitCts.Token).AsTask();
            var delayTask = Task.Delay(delay, waitCts.Token);
            await Task.WhenAny(triggerTask, delayTask);
            var triggered = triggerTask.IsCompletedSuccessfully;
            waitCts.Cancel();
            try
            {
                await (triggered ? delayTask : triggerTask);
            }
            catch (OperationCanceledException) when (waitCts.IsCancellationRequested)
            {
            }

            return triggered;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static bool TryGetRunAtLocalTime(string? value, out TimeSpan localTime)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            localTime = default;
            return false;
        }

        return TimeSpan.TryParse(value.Trim(), out localTime)
            && localTime >= TimeSpan.Zero
            && localTime < TimeSpan.FromDays(1);
    }
}
