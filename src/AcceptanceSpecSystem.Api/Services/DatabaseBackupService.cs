using AcceptanceSpecSystem.Api.Options;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 数据库定时备份服务。
/// </summary>
public sealed class DatabaseBackupService : BackgroundService
{
    private readonly DatabaseBackupManager _manager;
    private readonly ILogger<DatabaseBackupService> _logger;

    public DatabaseBackupService(
        DatabaseBackupManager manager,
        ILogger<DatabaseBackupService> logger)
    {
        _manager = manager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _manager.GetOptions();
            if (!options.Enabled)
            {
                await DelayOrStopAsync(TimeSpan.FromHours(1), stoppingToken);
                continue;
            }

            await DelayOrStopAsync(GetNextDelay(options), stoppingToken);
            if (stoppingToken.IsCancellationRequested)
                break;

            var result = await _manager.RunOnceAsync(stoppingToken);
            if (!result.Started)
            {
                _logger.LogDebug("数据库备份已有任务执行中，跳过本次自动备份");
            }
        }
    }

    private static TimeSpan GetNextDelay(DatabaseBackupOptions options)
    {
        if (!TryGetRunAtLocalTime(options.RunAtLocalTime, out var localTime))
        {
            localTime = TimeSpan.FromHours(2);
        }

        var now = TimeProvider.System.GetLocalNow().DateTime;
        var next = now.Date.Add(localTime);
        if (next <= now)
        {
            next = next.AddDays(1);
        }

        return next - now;
    }

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
