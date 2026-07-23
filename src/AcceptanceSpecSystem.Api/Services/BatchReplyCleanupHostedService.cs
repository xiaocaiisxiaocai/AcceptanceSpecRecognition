using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// BatchReply 清理的宿主调度适配器；清理判定与文件编排由 Application 用例拥有。
/// </summary>
public sealed class BatchReplyCleanupHostedService : BackgroundService
{
    private readonly IBatchReplyCleanupAppService _cleanupAppService;
    private readonly IOptionsMonitor<BatchReplyCleanupOptions> _optionsMonitor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BatchReplyCleanupHostedService> _logger;

    public BatchReplyCleanupHostedService(
        IBatchReplyCleanupAppService cleanupAppService,
        IOptionsMonitor<BatchReplyCleanupOptions> optionsMonitor,
        TimeProvider timeProvider,
        ILogger<BatchReplyCleanupHostedService> logger)
    {
        _cleanupAppService = cleanupAppService;
        _optionsMonitor = optionsMonitor;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initialDelay = TimeSpan.FromSeconds(Math.Max(0, _optionsMonitor.CurrentValue.InitialDelaySeconds));
        if (initialDelay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(initialDelay, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _optionsMonitor.CurrentValue;
            if (options.Enabled)
            {
                try
                {
                    await RunCleanupOnceAsync(options, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "BatchReply 后台清理轮次失败");
                }
            }

            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            var interval = TimeSpan.FromMinutes(Math.Max(1, options.CleanupIntervalMinutes));
            try
            {
                await Task.Delay(interval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public async Task<BatchReplyCleanupResult> RunCleanupOnceAsync(
        BatchReplyCleanupOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _cleanupAppService.CleanupAsync(
            new BatchReplyCleanupRequest(options.ObservationMode),
            cancellationToken);

        _logger.LogInformation(
            "BatchReply 后台清理完成: ObservationMode={ObservationMode}, Skipped={Skipped}, SessionScanned={SessionScanned}, ArtifactScanned={ArtifactScanned}, Eligible={Eligible}, Observed={Observed}, DeletedManifests={DeletedManifests}, DeletedFiles={DeletedFiles}, Retained={Retained}, Failures={Failures}, ElapsedMs={ElapsedMs}",
            result.ObservationMode,
            result.SkippedBecauseAlreadyRunning,
            result.SessionManifestsScanned,
            result.ArtifactManifestsScanned,
            result.EligibleManifests,
            result.ObservedManifests,
            result.DeletedManifests,
            result.DeletedFiles,
            result.RetainedManifests,
            result.FailureCount,
            result.Elapsed.TotalMilliseconds);

        return result;
    }
}
