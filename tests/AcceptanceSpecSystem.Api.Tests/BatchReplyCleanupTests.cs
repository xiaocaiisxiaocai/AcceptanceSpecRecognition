using System.Collections.Concurrent;
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
        store.Paths.Should().Contain("source-old.docx");
        store.Paths.Should().Contain("result-old.zip");
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
        store.DeleteFailures.Add("source-fail.docx");

        var service = CreateService(store);
        var result = await service.CleanupAsync(new BatchReplyCleanupRequest(ObservationMode: false));

        result.FailureCount.Should().Be(1);
        result.DeletedFiles.Should().Be(2, "同一清单的其他文件和后续清单不应被单文件故障阻断");
        result.DeletedManifests.Should().Be(1);
        store.Paths.Should().Contain(SessionManifestPath("failed-session"), "内容删除失败时保留清单供下轮重试");
        store.Paths.Should().NotContain(ArtifactManifestPath("successful-artifact"));
        store.Paths.Should().NotContain("result-good.zip");
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
