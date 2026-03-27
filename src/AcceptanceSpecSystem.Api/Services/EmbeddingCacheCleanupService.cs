using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 向量缓存自动清理服务
/// </summary>
public sealed class EmbeddingCacheCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<EmbeddingCacheCleanupOptions> _optionsMonitor;
    private readonly ILogger<EmbeddingCacheCleanupService> _logger;

    public EmbeddingCacheCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<EmbeddingCacheCleanupOptions> optionsMonitor,
        ILogger<EmbeddingCacheCleanupService> logger)
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
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var deleted = await unitOfWork.EmbeddingCaches.DeleteExpiredAsync(beforeTime);
            if (deleted > 0)
            {
                _logger.LogInformation("向量缓存自动清理完成：删除 {Count} 条过期缓存（早于 {BeforeTime:yyyy-MM-dd HH:mm:ss}）", deleted, beforeTime);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "向量缓存自动清理失败");
        }
    }
}
