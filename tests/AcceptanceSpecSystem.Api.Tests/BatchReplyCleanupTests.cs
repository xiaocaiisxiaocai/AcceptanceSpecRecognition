using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class BatchReplyCleanupTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ObservationMode_ShouldUseInjectedTimeAndKeepEligibleFiles()
    {
        var store = new FakeCleanupStore();
        AddSession(store, "expired-session", Now.UtcDateTime.AddHours(-2), "source-old.docx");
        AddSession(store, "active-session", Now.UtcDateTime.AddMinutes(-30), "source-active.docx");
        AddArtifact(store, "expired-artifact", Now.UtcDateTime.AddHours(-3), "result-old.zip");

        var service = CreateService(store);
        var result = await service.CleanupAsync(new BatchReplyCleanupRequest(ObservationMode: true));

        result.SessionManifestsScanned.Should().Be(2);
        result.ArtifactManifestsScanned.Should().Be(1);
        result.EligibleManifests.Should().Be(2);
        result.ObservedManifests.Should().Be(2);
        result.DeletedManifests.Should().Be(0);
        result.RetainedManifests.Should().Be(1);
        store.Paths.Should().Contain(StoredPath("word-files", "source-old.docx"));
        store.Paths.Should().Contain(StoredPath("filled-files", "result-old.zip"));
    }

    [Fact]
    public async Task DeleteMode_ShouldIsolateFileFailureAndContinueOtherManifests()
    {
        var store = new FakeCleanupStore();
        AddSession(
            store,
            "failed-session",
            Now.UtcDateTime.AddHours(-2),
            "source-fail.docx",
            "target-good.docx");
        AddArtifact(store, "successful-artifact", Now.UtcDateTime.AddHours(-3), "result-good.zip");
        store.DeleteFailures.Add(StoredPath("word-files", "source-fail.docx"));

        var service = CreateService(store);
        var result = await service.CleanupAsync(new BatchReplyCleanupRequest(ObservationMode: false));

        result.FailureCount.Should().Be(1);
        result.DeletedFiles.Should().Be(2, "同一清单的其他文件和后续清单不应被单文件故障阻断");
        result.DeletedManifests.Should().Be(1);
        store.Paths.Should().Contain(SessionManifestPath("failed-session"), "内容删除失败时保留清单供下轮重试");
        store.Paths.Should().NotContain(ArtifactManifestPath("successful-artifact"));
        store.Paths.Should().NotContain(StoredPath("filled-files", "result-good.zip"));
    }

    [Fact]
    public async Task Cancellation_ShouldStopScanAndReleaseRunGate()
    {
        var store = new FakeCleanupStore(blockReads: true);
        AddSession(store, "cancelled-session", Now.UtcDateTime.AddHours(-2), "source.docx");
        var service = CreateService(store);
        using var cancellation = new CancellationTokenSource();

        var cancelledRun = service.CleanupAsync(
            new BatchReplyCleanupRequest(ObservationMode: false),
            cancellation.Token);
        await store.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        Func<Task> waitForCancelledRun = async () => await cancelledRun;
        await waitForCancelledRun.Should().ThrowAsync<OperationCanceledException>();

        store.ReleaseReads();
        var retry = await service.CleanupAsync(new BatchReplyCleanupRequest(ObservationMode: true));
        retry.SkippedBecauseAlreadyRunning.Should().BeFalse("取消后必须释放防重入闸门");
    }

    [Fact]
    public async Task ConcurrentRun_ShouldBeSkippedWithoutStartingSecondScan()
    {
        var store = new FakeCleanupStore(blockReads: true);
        AddSession(store, "concurrent-session", Now.UtcDateTime.AddHours(-2), "source.docx");
        var service = CreateService(store);

        var firstRun = service.CleanupAsync(new BatchReplyCleanupRequest(ObservationMode: true));
        await store.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondRun = await service.CleanupAsync(new BatchReplyCleanupRequest(ObservationMode: true));
        secondRun.SkippedBecauseAlreadyRunning.Should().BeTrue();

        store.ReleaseReads();
        (await firstRun.WaitAsync(TimeSpan.FromSeconds(5))).SkippedBecauseAlreadyRunning.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteMode_ShouldRejectPathOutsideOwnedNamespace()
    {
        var store = new FakeCleanupStore();
        AddSession(store, "malicious-session", Now.UtcDateTime.AddHours(-2), "source.docx");
        var manifestPath = SessionManifestPath("malicious-session");
        var session = JsonSerializer.Deserialize<BatchReplySourceSession>(await store.ReadTextAsync(manifestPath, default))!;
        session.SourceFileRelativePath = "uploads/filled-files/2026-07-10/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.docx";
        store.Add(manifestPath, JsonSerializer.Serialize(session));
        store.Add(session.SourceFileRelativePath, "must-remain");

        var result = await CreateService(store).CleanupAsync(new BatchReplyCleanupRequest(false));

        result.FailureCount.Should().Be(1);
        store.Paths.Should().Contain(session.SourceFileRelativePath);
        store.Paths.Should().Contain(manifestPath, "未能安全删除全部内容时必须保留清单供人工检查");
    }

    [Fact]
    public async Task DeleteMode_ShouldKeepContentReferencedByAnotherOwner()
    {
        var store = new FakeCleanupStore();
        AddArtifact(store, "shared-artifact", Now.UtcDateTime.AddHours(-3), "shared.zip");
        var path = StoredPath("filled-files", "shared.zip");
        store.ReferencedPaths.Add(path);

        var result = await CreateService(store).CleanupAsync(new BatchReplyCleanupRequest(false));

        result.FailureCount.Should().Be(1);
        store.Paths.Should().Contain(path);
        store.Paths.Should().Contain(ArtifactManifestPath("shared-artifact"));
    }

    [Fact]
    public async Task DistributedDatabaseLockConflict_ShouldSkipWithoutScanning()
    {
        var store = new FakeCleanupStore { LeaseAvailable = false };
        AddSession(store, "lease-session", Now.UtcDateTime.AddHours(-2), "source.docx");

        var result = await CreateService(store).CleanupAsync(new BatchReplyCleanupRequest(false));

        result.SkippedBecauseAlreadyRunning.Should().BeTrue();
        result.SessionManifestsScanned.Should().Be(0);
    }

    [Fact]
    public async Task SessionCoordinator_ShouldSerializeSameSessionAcrossReplicaInstances()
    {
        var provider = new FakeDistributedLockProvider();
        var firstReplica = new BatchReplySessionCoordinator(provider);
        var secondReplica = new BatchReplySessionCoordinator(provider);

        await using var firstLease = await firstReplica.AcquireSessionAsync("same-session");
        Func<Task> competingAcquire = async () => await secondReplica.AcquireSessionAsync("same-session");

        await competingAcquire.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task HostedService_StopAsync_ShouldCancelActiveApplicationCleanup()
    {
        var cleanup = new BlockingCleanupAppService();
        var options = new StaticOptionsMonitor<BatchReplyCleanupOptions>(new BatchReplyCleanupOptions
        {
            Enabled = true,
            ObservationMode = true,
            InitialDelaySeconds = 0,
            CleanupIntervalMinutes = 15
        });
        using var hostedService = new BatchReplyCleanupHostedService(
            cleanup,
            options,
            TimeProvider.System,
            NullLogger<BatchReplyCleanupHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);
        await cleanup.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await hostedService.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        cleanup.CancellationObserved.Should().BeTrue();
        cleanup.InvocationCount.Should().Be(1, "宿主停止后不得启动新一轮扫描");
    }

    private static BatchReplyCleanupAppService CreateService(FakeCleanupStore store)
    {
        return new BatchReplyCleanupAppService(
            store,
            new BatchReplyRetentionPolicy(TimeSpan.FromHours(1), TimeSpan.FromHours(2)),
            new BatchReplySessionCoordinator(),
            new FixedTimeProvider(Now),
            NullLogger<BatchReplyCleanupAppService>.Instance);
    }

    private static void AddSession(
        FakeCleanupStore store,
        string sessionId,
        DateTime updatedAt,
        string sourcePath,
        params string[] targetPaths)
    {
        sourcePath = StoredPath("word-files", sourcePath);
        targetPaths = targetPaths.Select(path => StoredPath("word-files", path)).ToArray();
        var manifestPath = SessionManifestPath(sessionId);
        var session = new BatchReplySourceSession
        {
            SessionId = sessionId,
            OwnerUserId = 1,
            OwnerCompanyId = 1,
            SourceFileRelativePath = sourcePath,
            ManifestRelativePath = manifestPath,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt,
            TargetFiles = targetPaths.Select((path, index) => new BatchReplyTargetFile
            {
                TargetId = $"target-{index}",
                RelativePath = path
            }).ToList()
        };

        store.Add(manifestPath, JsonSerializer.Serialize(session));
        store.Add(sourcePath, "source");
        foreach (var targetPath in targetPaths)
        {
            store.Add(targetPath, "target");
        }
    }

    private static void AddArtifact(FakeCleanupStore store, string taskId, DateTime createdAt, string resultPath)
    {
        resultPath = StoredPath("filled-files", resultPath);
        var manifestPath = ArtifactManifestPath(taskId);
        var artifact = new BatchReplyDownloadArtifact
        {
            TaskId = taskId,
            OwnerUserId = 1,
            OwnerCompanyId = 1,
            RelativePath = resultPath,
            ManifestRelativePath = manifestPath,
            FileName = "result.zip",
            CreatedAt = createdAt
        };

        store.Add(manifestPath, JsonSerializer.Serialize(artifact));
        store.Add(resultPath, "artifact");
    }

    private static string SessionManifestPath(string id) =>
        $"{BatchReplyCleanupAppService.SessionManifestDirectory}/{id}.json";

    private static string ArtifactManifestPath(string id) =>
        $"{BatchReplyCleanupAppService.ArtifactManifestDirectory}/{id}.json";

    private static string StoredPath(string bucket, string seed)
    {
        var guidBytes = MD5.HashData(Encoding.UTF8.GetBytes(seed));
        return $"uploads/{bucket}/2026-07-10/{new Guid(guidBytes):N}{Path.GetExtension(seed)}";
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeCleanupStore(bool blockReads = false) : IBatchReplyCleanupStore
    {
        private readonly ConcurrentDictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly TaskCompletionSource _releaseReads = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public HashSet<string> DeleteFailures { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ReferencedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool LeaseAvailable { get; set; } = true;
        public IReadOnlyCollection<string> Paths => _files.Keys.ToArray();

        public void Add(string path, string content) => _files[path] = content;

        public void ReleaseReads() => _releaseReads.TrySetResult();

        public IReadOnlyList<string> EnumerateManifestPaths(string relativeDirectory)
        {
            return _files.Keys
                .Where(path => path.StartsWith($"{relativeDirectory}/", StringComparison.OrdinalIgnoreCase) &&
                               path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        public async Task<string> ReadTextAsync(string relativePath, CancellationToken cancellationToken)
        {
            if (blockReads)
            {
                ReadStarted.TrySetResult();
                await _releaseReads.Task.WaitAsync(cancellationToken);
            }

            return _files[relativePath];
        }

        public Task<bool> DeleteIfExistsAsync(string relativePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DeleteFailures.Contains(relativePath))
            {
                throw new IOException("injected delete failure");
            }

            return Task.FromResult(_files.TryRemove(relativePath, out _));
        }

        public Task<bool> IsContentPathReferencedAsync(
            string relativePath,
            string excludingManifestPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ReferencedPaths.Contains(relativePath));
        }

        public Task<IBatchReplyCleanupLease?> TryAcquireCleanupLeaseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IBatchReplyCleanupLease?>(LeaseAvailable ? new FakeLease() : null);
        }

        private sealed class FakeLease : IBatchReplyCleanupLease
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeDistributedLockProvider : IBatchReplyDistributedLockProvider
    {
        private readonly HashSet<string> _held = new(StringComparer.Ordinal);

        public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan waitTimeout, CancellationToken cancellationToken)
        {
            lock (_held)
            {
                if (!_held.Add(key)) return Task.FromResult<IAsyncDisposable?>(null);
            }
            return Task.FromResult<IAsyncDisposable?>(new Lease(_held, key));
        }

        private sealed class Lease(HashSet<string> held, string key) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                lock (held) { held.Remove(key); }
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class BlockingCleanupAppService : IBatchReplyCleanupAppService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }
        public int InvocationCount { get; private set; }

        public async Task<BatchReplyCleanupResult> CleanupAsync(
            BatchReplyCleanupRequest request,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }

            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
