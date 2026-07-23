using AcceptanceSpecSystem.Api.Options;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 审计日志自动清理服务
/// </summary>
public sealed class AuditLogCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AuditLogOptions> _optionsMonitor;
    private readonly ILogger<AuditLogCleanupService> _logger;

    public AuditLogCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AuditLogOptions> optionsMonitor,
        ILogger<AuditLogCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CleanupOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _optionsMonitor.CurrentValue;
            var intervalHours = Math.Max(1, options.CleanupIntervalHours);

            try
            {
                await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            await CleanupOnceAsync(stoppingToken);
        }
    }

    private async Task CleanupOnceAsync(CancellationToken stoppingToken)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.EnableAutoCleanup)
            return;

        var retentionDays = Math.Max(1, options.RetentionDays);
        var beforeTime = DateTime.UtcNow.AddDays(-retentionDays);
        var batchSize = Math.Clamp(options.CleanupBatchSize, 1, 1000);
        var maxBatches = Math.Clamp(options.MaxBatchesPerRun, 1, 100);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var appService = scope.ServiceProvider.GetRequiredService<IAuditLogRetentionAppService>();
            var totalDeleted = 0;
            for (var batch = 0; batch < maxBatches && !stoppingToken.IsCancellationRequested; batch++)
            {
                var deleted = await appService.DeleteBeforeAsync(beforeTime, batchSize, stoppingToken);
                totalDeleted += deleted;
                if (deleted < batchSize)
                    break;
            }

            for (var batch = 0; batch < maxBatches && !stoppingToken.IsCancellationRequested; batch++)
            {
                var deleted = await appService.DeleteOverflowAsync(
                    Math.Max(1, options.MaxRecordCount),
                    batchSize,
                    stoppingToken);
                totalDeleted += deleted;
                if (deleted < batchSize)
                    break;
            }

            if (totalDeleted > 0)
            {
                _logger.LogInformation("审计日志自动清理完成：分批删除 {Count} 条（早于 {BeforeTime:yyyy-MM-dd HH:mm:ss}）", totalDeleted, beforeTime);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "审计日志自动清理失败");
        }
    }
}
