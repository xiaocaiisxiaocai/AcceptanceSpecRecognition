using AcceptanceSpecSystem.Application.Options;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class AcceptanceSpecCleanupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AcceptanceSpecCleanupOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AcceptanceSpecCleanupHostedService> _logger;
    private DateTimeOffset _lastRetentionCleanup = DateTimeOffset.MinValue;

    public AcceptanceSpecCleanupHostedService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AcceptanceSpecCleanupOptions> options,
        TimeProvider timeProvider,
        ILogger<AcceptanceSpecCleanupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IAcceptanceSpecCleanupAppService>();
                processed = await service.ProcessNextScanBatchAsync(stoppingToken);
                var now = _timeProvider.GetUtcNow();
                if (!processed && now - _lastRetentionCleanup >= TimeSpan.FromHours(1))
                {
                    await service.CleanupExpiredScansAsync(stoppingToken);
                    _lastRetentionCleanup = now;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验收规格清理扫描后台循环失败");
            }

            var delay = processed
                ? TimeSpan.FromMilliseconds(25)
                : TimeSpan.FromMilliseconds(_options.CurrentValue.PollIntervalMilliseconds);
            await Task.Delay(delay, _timeProvider, stoppingToken);
        }
    }
}
