using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 分批清理过期执行历史；关联归档由现有孤儿文件巡检按引用状态回收。
/// </summary>
public sealed class ExecutionHistoryCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<ExecutionHistoryRetentionOptions> _options;
    private readonly ILogger<ExecutionHistoryCleanupService> _logger;

    public ExecutionHistoryCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ExecutionHistoryRetentionOptions> options,
        ILogger<ExecutionHistoryCleanupService> logger)
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

        var beforeTime = DateTime.UtcNow.AddDays(-Math.Clamp(options.RetentionDays, 1, 3650));
        var batchSize = Math.Clamp(options.BatchSize, 1, 1000);
        var maxBatches = Math.Clamp(options.MaxBatchesPerRun, 1, 100);
        var totalDeleted = 0;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var appService = scope.ServiceProvider.GetRequiredService<IExecutionHistoryRetentionAppService>();
            for (var batch = 0; batch < maxBatches && !cancellationToken.IsCancellationRequested; batch++)
            {
                var deleted = await appService.DeleteBeforeAsync(beforeTime, batchSize, cancellationToken);
                totalDeleted += deleted;
                if (deleted < batchSize)
                    break;
            }

            for (var batch = 0; batch < maxBatches && !cancellationToken.IsCancellationRequested; batch++)
            {
                var deleted = await appService.DeleteOverflowAsync(
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
                    "执行历史自动清理完成：分批删除 {Count} 条（早于 {BeforeTime:yyyy-MM-dd HH:mm:ss}）",
                    totalDeleted,
                    beforeTime);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("执行历史自动清理失败: exceptionType={ExceptionType}", ex.GetType().Name);
        }
    }
}
