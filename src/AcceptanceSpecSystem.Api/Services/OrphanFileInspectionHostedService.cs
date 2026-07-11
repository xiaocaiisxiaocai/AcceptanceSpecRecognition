using System.Diagnostics;
using System.Diagnostics.Metrics;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class OrphanFileInspectionHostedService : BackgroundService
{
    public const string MeterName = "AcceptanceSpecSystem.OrphanFiles";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<OrphanFileInspectionOptions> _optionsMonitor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrphanFileInspectionHostedService> _logger;
    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _scanned;
    private readonly Counter<long> _retained;
    private readonly Counter<long> _referenced;
    private readonly Counter<long> _eligible;
    private readonly Counter<long> _deleted;
    private readonly Counter<long> _failures;

    public OrphanFileInspectionHostedService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<OrphanFileInspectionOptions> optionsMonitor,
        TimeProvider timeProvider,
        ILogger<OrphanFileInspectionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _optionsMonitor = optionsMonitor;
        _timeProvider = timeProvider;
        _logger = logger;
        _scanned = _meter.CreateCounter<long>("orphan_files_scanned_total");
        _retained = _meter.CreateCounter<long>("orphan_files_retained_total");
        _referenced = _meter.CreateCounter<long>("orphan_files_referenced_total");
        _eligible = _meter.CreateCounter<long>("orphan_files_eligible_total");
        _deleted = _meter.CreateCounter<long>("orphan_files_deleted_total");
        _failures = _meter.CreateCounter<long>("orphan_files_failures_total");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initialDelay = TimeSpan.FromSeconds(Math.Max(0, _optionsMonitor.CurrentValue.InitialDelaySeconds));
        if (initialDelay > TimeSpan.Zero)
        {
            await Task.Delay(initialDelay, _timeProvider, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _optionsMonitor.CurrentValue;
            if (options.Enabled)
            {
                await RunInspectionOnceAsync(options, stoppingToken);
            }

            var interval = TimeSpan.FromMinutes(Math.Max(1, options.InspectionIntervalMinutes));
            await Task.Delay(interval, _timeProvider, stoppingToken);
        }
    }

    public async Task<OrphanFileInspectionResult> RunInspectionOnceAsync(
        OrphanFileInspectionOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = _scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrphanFileInspectionAppService>();
        var result = await service.InspectAsync(
            new OrphanFileInspectionRequest(
                options.ObservationMode,
                TimeSpan.FromHours(options.GracePeriodHours)),
            cancellationToken);

        if (!result.SkippedBecauseAlreadyRunning)
        {
            RecordMetrics(result);
            _logger.LogInformation(
                "孤儿文件巡检完成: ObservationMode={ObservationMode}, Scanned={Scanned}, Retained={Retained}, Referenced={Referenced}, Eligible={Eligible}, Deleted={Deleted}, Failures={Failures}, ElapsedMs={ElapsedMs}",
                result.ObservationMode,
                result.Scanned,
                result.Retained,
                result.Referenced,
                result.Eligible,
                result.Deleted,
                result.Failures,
                result.Elapsed.TotalMilliseconds);
        }

        return result;
    }

    private void RecordMetrics(OrphanFileInspectionResult result)
    {
        var tags = new TagList { { "observation_mode", result.ObservationMode } };
        _scanned.Add(result.Scanned, tags);
        _retained.Add(result.Retained, tags);
        _referenced.Add(result.Referenced, tags);
        _eligible.Add(result.Eligible, tags);
        _deleted.Add(result.Deleted, tags);
        _failures.Add(result.Failures, tags);
    }

    public override void Dispose()
    {
        _meter.Dispose();
        base.Dispose();
    }
}
