using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class WordFileDeletionCleanupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public WordFileDeletionCleanupHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initialDelay = ReadOptions().InitialDelaySeconds;
        if (initialDelay > 0)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(initialDelay), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = ReadOptions();
            if (options.Enabled)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IWordFileDeletionCleanupAppService>();
                    await service.RunBatchAsync(options.BatchSize, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    using var logScope = _scopeFactory.CreateScope();
                    logScope.ServiceProvider
                        .GetRequiredService<ILogger<WordFileDeletionCleanupHostedService>>()
                        .LogError(exception, "持久文件后台清理轮次失败");
                }
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromMinutes(Math.Max(1, options.CleanupIntervalMinutes)),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private WordFileDeletionCleanupOptions ReadOptions()
    {
        using var scope = _scopeFactory.CreateScope();
        return scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<WordFileDeletionCleanupOptions>>()
            .CurrentValue;
    }
}
