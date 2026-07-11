using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Application.Services;

public sealed record BatchReplyRetentionPolicy(
    TimeSpan SessionRetention,
    TimeSpan ArtifactRetention)
{
    public static BatchReplyRetentionPolicy Default { get; } = new(
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(24));
}

public sealed class BatchReplySessionCoordinator
{
    private readonly ReferenceCountedKeyedLock<string> _locks = new(StringComparer.Ordinal);

    public ValueTask<IDisposable> AcquireSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        return _locks.AcquireAsync($"session:{sessionId}", cancellationToken);
    }

    public ValueTask<IDisposable> AcquireArtifactAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        return _locks.AcquireAsync($"artifact:{taskId}", cancellationToken);
    }
}

public interface IBatchReplyCleanupStore
{
    IReadOnlyList<string> EnumerateManifestPaths(string relativeDirectory);

    Task<string> ReadTextAsync(string relativePath, CancellationToken cancellationToken);

    Task<bool> DeleteIfExistsAsync(string relativePath, CancellationToken cancellationToken);
}

public sealed record BatchReplyCleanupRequest(bool ObservationMode);

public sealed record BatchReplyCleanupResult(
    bool SkippedBecauseAlreadyRunning,
    bool ObservationMode,
    int SessionManifestsScanned,
    int ArtifactManifestsScanned,
    int EligibleManifests,
    int ObservedManifests,
    int DeletedManifests,
    int DeletedFiles,
    int RetainedManifests,
    int FailureCount,
    TimeSpan Elapsed);

public interface IBatchReplyCleanupAppService
{
    Task<BatchReplyCleanupResult> CleanupAsync(
        BatchReplyCleanupRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 批量回复会话与下载产物的周期清理用例。宿主只负责调度和转发取消信号。
/// </summary>
public sealed class BatchReplyCleanupAppService : IBatchReplyCleanupAppService
{
    public const string SessionManifestDirectory = "uploads/batch-reply/sessions";
    public const string ArtifactManifestDirectory = "uploads/filled-files/manifests";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IBatchReplyCleanupStore _store;
    private readonly BatchReplyRetentionPolicy _retentionPolicy;
    private readonly BatchReplySessionCoordinator _coordinator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BatchReplyCleanupAppService> _logger;
    private readonly SemaphoreSlim _runGate = new(1, 1);

    public BatchReplyCleanupAppService(
        IBatchReplyCleanupStore store,
        BatchReplyRetentionPolicy retentionPolicy,
        BatchReplySessionCoordinator coordinator,
        TimeProvider timeProvider,
        ILogger<BatchReplyCleanupAppService> logger)
    {
        _store = store;
        _retentionPolicy = retentionPolicy;
        _coordinator = coordinator;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<BatchReplyCleanupResult> CleanupAsync(
        BatchReplyCleanupRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = _timeProvider.GetTimestamp();
        if (!await _runGate.WaitAsync(0, cancellationToken))
        {
            return new BatchReplyCleanupResult(
                true,
                request.ObservationMode,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                _timeProvider.GetElapsedTime(startedAt));
        }

        var metrics = new CleanupMetrics(request.ObservationMode);
        try
        {
            await ScanSessionsAsync(metrics, cancellationToken);
            await ScanArtifactsAsync(metrics, cancellationToken);
            return metrics.ToResult(_timeProvider.GetElapsedTime(startedAt));
        }
        finally
        {
            _runGate.Release();
        }
    }

    private async Task ScanSessionsAsync(CleanupMetrics metrics, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> manifestPaths;
        try
        {
            manifestPaths = _store.EnumerateManifestPaths(SessionManifestDirectory);
        }
        catch (Exception ex)
        {
            metrics.FailureCount++;
            _logger.LogWarning(ex, "枚举批量回复会话清单失败");
            return;
        }

        foreach (var manifestPath in manifestPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            metrics.SessionManifestsScanned++;
            try
            {
                var session = await ReadSessionAsync(manifestPath, cancellationToken);
                if (session == null)
                {
                    metrics.FailureCount++;
                    continue;
                }

                if (!IsExpired(session.UpdatedAt, _retentionPolicy.SessionRetention))
                {
                    metrics.RetainedManifests++;
                    continue;
                }

                var manifestId = Path.GetFileNameWithoutExtension(manifestPath);
                using var sessionLock = await _coordinator.AcquireSessionAsync(manifestId, cancellationToken);
                session = await ReadSessionAsync(manifestPath, cancellationToken);
                if (session == null ||
                    !string.Equals(session.SessionId, manifestId, StringComparison.Ordinal) ||
                    !IsExpired(session.UpdatedAt, _retentionPolicy.SessionRetention))
                {
                    metrics.RetainedManifests++;
                    continue;
                }

                var contentPaths = session.TargetFiles
                    .Select(file => file.RelativePath)
                    .Append(session.SourceFileRelativePath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Cast<string>();

                await DeleteEligibleManifestAsync(
                    "session",
                    manifestId,
                    manifestPath,
                    contentPaths,
                    metrics,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                metrics.FailureCount++;
                _logger.LogWarning(ex, "处理批量回复会话清单失败: {ManifestId}", Path.GetFileNameWithoutExtension(manifestPath));
            }
        }
    }

    private async Task ScanArtifactsAsync(CleanupMetrics metrics, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> manifestPaths;
        try
        {
            manifestPaths = _store.EnumerateManifestPaths(ArtifactManifestDirectory);
        }
        catch (Exception ex)
        {
            metrics.FailureCount++;
            _logger.LogWarning(ex, "枚举批量回复下载产物清单失败");
            return;
        }

        foreach (var manifestPath in manifestPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            metrics.ArtifactManifestsScanned++;
            try
            {
                var artifact = await ReadArtifactAsync(manifestPath, cancellationToken);
                if (artifact == null)
                {
                    metrics.FailureCount++;
                    continue;
                }

                if (!IsExpired(artifact.CreatedAt, _retentionPolicy.ArtifactRetention))
                {
                    metrics.RetainedManifests++;
                    continue;
                }

                var manifestId = Path.GetFileNameWithoutExtension(manifestPath);
                using var artifactLock = await _coordinator.AcquireArtifactAsync(manifestId, cancellationToken);
                artifact = await ReadArtifactAsync(manifestPath, cancellationToken);
                if (artifact == null ||
                    !string.Equals(artifact.TaskId, manifestId, StringComparison.Ordinal) ||
                    !IsExpired(artifact.CreatedAt, _retentionPolicy.ArtifactRetention))
                {
                    metrics.RetainedManifests++;
                    continue;
                }

                await DeleteEligibleManifestAsync(
                    "artifact",
                    manifestId,
                    manifestPath,
                    [artifact.RelativePath],
                    metrics,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                metrics.FailureCount++;
                _logger.LogWarning(ex, "处理批量回复下载产物清单失败: {ManifestId}", Path.GetFileNameWithoutExtension(manifestPath));
            }
        }
    }

    private async Task DeleteEligibleManifestAsync(
        string manifestType,
        string manifestId,
        string manifestPath,
        IEnumerable<string> contentPaths,
        CleanupMetrics metrics,
        CancellationToken cancellationToken)
    {
        metrics.EligibleManifests++;
        if (metrics.ObservationMode)
        {
            metrics.ObservedManifests++;
            return;
        }

        var contentDeleteFailed = false;
        foreach (var contentPath in contentPaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await _store.DeleteIfExistsAsync(contentPath, cancellationToken))
                {
                    metrics.DeletedFiles++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                contentDeleteFailed = true;
                metrics.FailureCount++;
                _logger.LogWarning(ex, "删除批量回复过期文件失败: {ManifestType} {ManifestId}", manifestType, manifestId);
            }
        }

        if (contentDeleteFailed)
        {
            return;
        }

        try
        {
            if (await _store.DeleteIfExistsAsync(manifestPath, cancellationToken))
            {
                metrics.DeletedManifests++;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            metrics.FailureCount++;
            _logger.LogWarning(ex, "删除批量回复过期清单失败: {ManifestType} {ManifestId}", manifestType, manifestId);
        }
    }

    private async Task<BatchReplySourceSession?> ReadSessionAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var payload = await _store.ReadTextAsync(manifestPath, cancellationToken);
        return JsonSerializer.Deserialize<BatchReplySourceSession>(payload, JsonOptions);
    }

    private async Task<BatchReplyDownloadArtifact?> ReadArtifactAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var payload = await _store.ReadTextAsync(manifestPath, cancellationToken);
        return JsonSerializer.Deserialize<BatchReplyDownloadArtifact>(payload, JsonOptions);
    }

    private bool IsExpired(DateTime timestamp, TimeSpan retention)
    {
        return _timeProvider.GetUtcNow().UtcDateTime - timestamp >= retention;
    }

    private sealed class CleanupMetrics(bool observationMode)
    {
        public bool ObservationMode { get; } = observationMode;
        public int SessionManifestsScanned { get; set; }
        public int ArtifactManifestsScanned { get; set; }
        public int EligibleManifests { get; set; }
        public int ObservedManifests { get; set; }
        public int DeletedManifests { get; set; }
        public int DeletedFiles { get; set; }
        public int RetainedManifests { get; set; }
        public int FailureCount { get; set; }

        public BatchReplyCleanupResult ToResult(TimeSpan elapsed)
        {
            return new BatchReplyCleanupResult(
                false,
                ObservationMode,
                SessionManifestsScanned,
                ArtifactManifestsScanned,
                EligibleManifests,
                ObservedManifests,
                DeletedManifests,
                DeletedFiles,
                RetainedManifests,
                FailureCount,
                elapsed);
        }
    }
}
