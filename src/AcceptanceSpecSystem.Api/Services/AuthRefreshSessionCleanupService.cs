using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class AuthRefreshSessionCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AuthRefreshSessionRetentionOptions> _options;
    private readonly ILogger<AuthRefreshSessionCleanupService> _logger;

    public AuthRefreshSessionCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AuthRefreshSessionRetentionOptions> options,
        ILogger<AuthRefreshSessionCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CleanupOnceAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromHours(Math.Clamp(_options.CurrentValue.CleanupIntervalHours, 1, 168)),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await CleanupOnceAsync(stoppingToken);
        }
    }

    internal async Task CleanupOnceAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled)
            return;

        var beforeTime = DateTime.UtcNow.AddDays(-Math.Clamp(options.RetentionDaysAfterExpiry, 1, 3650));
        var batchSize = Math.Clamp(options.BatchSize, 1, 1000);
        var maxBatches = Math.Clamp(options.MaxBatchesPerRun, 1, 100);
        var totalDeleted = 0;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sessions = scope.ServiceProvider.GetRequiredService<IAuthRefreshSessionService>();
            for (var batch = 0; batch < maxBatches && !cancellationToken.IsCancellationRequested; batch++)
            {
                var deleted = await sessions.DeleteExpiredBeforeAsync(beforeTime, batchSize, cancellationToken);
                totalDeleted += deleted;
                if (deleted < batchSize)
                    break;
            }

            for (var batch = 0; batch < maxBatches && !cancellationToken.IsCancellationRequested; batch++)
            {
                var deleted = await sessions.DeleteOverflowAsync(
                    Math.Max(1, options.MaxRecordCount),
                    batchSize,
                    cancellationToken);
                totalDeleted += deleted;
                if (deleted < batchSize)
                    break;
            }

            if (totalDeleted > 0)
            {
                _logger.LogInformation(
                    "刷新会话自动清理完成：分批删除 {Count} 条过期记录（早于 {BeforeTime:yyyy-MM-dd HH:mm:ss}）",
                    totalDeleted,
                    beforeTime);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("刷新会话自动清理失败: exceptionType={ExceptionType}", ex.GetType().Name);
        }
    }
}
