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

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var appService = scope.ServiceProvider.GetRequiredService<IAuditLogRetentionAppService>();
            var deleted = await appService.DeleteBeforeAsync(beforeTime, stoppingToken);
            if (deleted > 0)
            {
                _logger.LogInformation("审计日志自动清理完成：删除 {Count} 条（早于 {BeforeTime:yyyy-MM-dd HH:mm:ss}）", deleted, beforeTime);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "审计日志自动清理失败");
        }
    }
}
