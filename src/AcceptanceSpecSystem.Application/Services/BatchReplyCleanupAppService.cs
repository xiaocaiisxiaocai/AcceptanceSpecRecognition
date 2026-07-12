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
    private readonly IBatchReplyDistributedLockProvider _distributedLocks;

    public BatchReplySessionCoordinator(IBatchReplyDistributedLockProvider? distributedLocks = null)
    {
        _distributedLocks = distributedLocks ?? NoOpBatchReplyDistributedLockProvider.Instance;
    }

    public async ValueTask<IAsyncDisposable> AcquireSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        return await AcquireAsync($"session:{sessionId}", cancellationToken);
    }

    public async ValueTask<IAsyncDisposable> AcquireArtifactAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        return await AcquireAsync($"artifact:{taskId}", cancellationToken);
    }

    private async ValueTask<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var local = await _locks.AcquireAsync(key, cancellationToken);
        try
        {
            var distributed = await _distributedLocks.TryAcquireAsync(key, TimeSpan.FromSeconds(10), cancellationToken)
                ?? throw new TimeoutException($"等待 BatchReply 分布式锁超时: {key}");
            return new CompositeBatchReplyLock(local, distributed);
        }
        catch
        {
            local.Dispose();
            throw;
        }
    }
}

public interface IBatchReplyDistributedLockProvider
{
    Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan waitTimeout, CancellationToken cancellationToken);
}

internal sealed class NoOpBatchReplyDistributedLockProvider : IBatchReplyDistributedLockProvider
{
    public static NoOpBatchReplyDistributedLockProvider Instance { get; } = new();
    public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan waitTimeout, CancellationToken cancellationToken) =>
        Task.FromResult<IAsyncDisposable?>(new NoOpLease());

    private sealed class NoOpLease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class CompositeBatchReplyLock(IDisposable local, IAsyncDisposable distributed) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        try { await distributed.DisposeAsync(); }
        finally { local.Dispose(); }
    }
}

public interface IBatchReplyCleanupStore
{
    IReadOnlyList<string> EnumerateManifestPaths(string relativeDirectory);

    Task<string> ReadTextAsync(string relativePath, CancellationToken cancellationToken);

    Task<bool> DeleteIfExistsAsync(string relativePath, CancellationToken cancellationToken);

    Task<bool> IsContentPathReferencedAsync(
        string relativePath,
        string excludingManifestPath,
        CancellationToken cancellationToken);

    Task<IBatchReplyCleanupLease?> TryAcquireCleanupLeaseAsync(CancellationToken cancellationToken);
}

public interface IBatchReplyCleanupLease : IAsyncDisposable;

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
            await using var distributedLease = await _store.TryAcquireCleanupLeaseAsync(cancellationToken);
            if (distributedLease == null)
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
                await using var sessionLock = await _coordinator.AcquireSessionAsync(manifestId, cancellationToken);
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
                await using var artifactLock = await _coordinator.AcquireArtifactAsync(manifestId, cancellationToken);
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
                if (!IsOwnedContentPath(manifestType, contentPath) ||
                    await _store.IsContentPathReferencedAsync(contentPath, manifestPath, cancellationToken))
                {
                    contentDeleteFailed = true;
                    metrics.FailureCount++;
                    _logger.LogWarning(
                        "拒绝删除不属于清单命名空间或仍被其他清单/数据库引用的文件: {ManifestType} {ManifestId} {ContentPath}",
                        manifestType,
                        manifestId,
                        contentPath);
                    continue;
                }

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

    internal static bool IsOwnedContentPath(string manifestType, string contentPath)
    {
        if (string.IsNullOrWhiteSpace(contentPath) ||
            Path.IsPathRooted(contentPath) ||
            contentPath.Contains('\\', StringComparison.Ordinal) ||
            contentPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            return false;
        }

        var segments = contentPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 4 ||
            !string.Equals(segments[0], "uploads", StringComparison.Ordinal) ||
            !DateOnly.TryParseExact(segments[2], "yyyy-MM-dd", out _))
        {
            return false;
        }

        var fileName = segments[3];
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (Path.GetFileName(fileName) != fileName || !Guid.TryParseExact(stem, "N", out _))
        {
            return false;
        }

        return manifestType switch
        {
            "session" => segments[1] is "word-files" or "excel-files",
            "artifact" => segments[1] == "filled-files",
            _ => false
        };
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
