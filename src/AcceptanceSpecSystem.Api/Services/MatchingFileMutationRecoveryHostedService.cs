using AcceptanceSpecSystem.Application.Services;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 启动时恢复在物理 Excel 替换后、最终数据库事务提交前中断的填充任务。
/// </summary>
public sealed class MatchingFileMutationRecoveryHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MatchingFileMutationRecoveryHostedService> _logger;

    public MatchingFileMutationRecoveryHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<MatchingFileMutationRecoveryHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var snapshotService = scope.ServiceProvider.GetRequiredService<MatchingTaskSnapshotService>();
            await snapshotService.RecoverAllPendingFileMutationsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "启动时恢复未完成 Excel 写回失败");
        }
    }
}
