using AcceptanceSpecSystem.Api.Options;

namespace AcceptanceSpecSystem.Api.Services;

public interface IEmbeddingCacheWarmupExecutor
{
    Task WarmupAsync(int batchSize, int maxItemsPerRun, CancellationToken cancellationToken);
}

/// <summary>
/// 向量缓存后台预热服务
/// </summary>
public sealed class EmbeddingCacheWarmupService : BackgroundService
{
    private readonly EmbeddingCacheWarmupManager _manager;
    private readonly ILogger<EmbeddingCacheWarmupService> _logger;

    public EmbeddingCacheWarmupService(
        EmbeddingCacheWarmupManager manager,
        ILogger<EmbeddingCacheWarmupService> logger)
    {
        _manager = manager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startupHandled = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _manager.GetOptions();
            if (!options.Enabled)
            {
                await DelayOrStopAsync(GetIntervalDelay(options), stoppingToken);
                continue;
            }

            if (!startupHandled)
            {
                startupHandled = true;
                if (options.RunOnStartup)
                {
                    await WarmupOnceAsync(options, stoppingToken);
                    continue;
                }
            }

            await DelayOrStopAsync(GetNextDelay(options), stoppingToken);
            await WarmupOnceAsync(_manager.GetOptions(), stoppingToken);
        }
    }

    private async Task WarmupOnceAsync(EmbeddingCacheWarmupOptions options, CancellationToken stoppingToken)
    {
        if (!options.Enabled || stoppingToken.IsCancellationRequested)
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

    private static async Task DelayOrStopAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, stoppingToken);
        }
        catch (TaskCanceledException)
        {
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
