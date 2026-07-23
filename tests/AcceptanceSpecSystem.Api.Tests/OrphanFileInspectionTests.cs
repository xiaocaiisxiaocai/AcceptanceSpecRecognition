using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class OrphanFileInspectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ObservationMode_ShouldClassifyDatabaseManifestGraceAndEligibleFilesWithoutDeleting()
    {
        var files = new[]
        {
            Old("uploads/word-files/2026-01-01/db.docx"),
            Old("uploads/filled-files/2026-01-01/manifest.docx"),
            Old("uploads/excel-files/2026-01-01/orphan.xlsx"),
            New("uploads/word-files/2026-07-11/new.docx"),
            Old("../outside.docx")
        };
        var store = new FakeStore(files)
        {
            ManifestSnapshot = CompleteSnapshot("uploads/filled-files/2026-01-01/manifest.docx")
        };
        var database = new FakeDatabaseReferences
        {
            Snapshot = CompleteSnapshot("uploads/word-files/2026-01-01/db.docx")
        };
        var service = CreateService(store, database);

        await service.InspectAsync(new OrphanFileInspectionRequest(true, TimeSpan.FromDays(7)));
        var result = await service.InspectAsync(new OrphanFileInspectionRequest(true, TimeSpan.FromDays(7)));

        result.Scanned.Should().Be(5);
        result.Referenced.Should().Be(2);
        result.Eligible.Should().Be(1);
        result.Deleted.Should().Be(0);
        result.Retained.Should().Be(5);
        store.DeleteAttempts.Should().BeEmpty();
    }

    [Fact]
    public async Task ActiveMode_ShouldFailClosedWhenManifestCoverageIsIncomplete()
    {
        var filled = Old("uploads/filled-files/2026-01-01/candidate.docx");
        var uploaded = Old("uploads/word-files/2026-01-01/candidate.docx");
        var store = new FakeStore([filled, uploaded])
        {
            ManifestSnapshot = new OrphanReferenceSnapshot(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    OrphanFilePathRules.FilledFilesNamespace
                },
                1)
        };
        var service = CreateService(store, new FakeDatabaseReferences());

        await service.InspectAsync(new OrphanFileInspectionRequest(true, TimeSpan.FromDays(7)));
        var result = await service.InspectAsync(new OrphanFileInspectionRequest(false, TimeSpan.FromDays(7)));

        result.Failures.Should().Be(1);
        result.Deleted.Should().Be(1, "完整的上传文件命名空间仍可独立证明");
        result.Retained.Should().Be(1, "manifest 覆盖不完整的 filled-files 必须 fail closed");
        store.DeleteAttempts.Should().ContainSingle().Which.Should().Be(uploaded.RelativePath);
    }

    [Fact]
    public async Task Delete_ShouldRecheckReferencesAndRetainWhenDatabaseReferenceAppearsDuringScan()
    {
        var candidate = Old("uploads/word-files/2026-01-01/raced.docx");
        var store = new FakeStore([candidate]);
        var database = new FakeDatabaseReferences
        {
            Probe = _ => new OrphanReferenceProbe(true, true)
        };
        var service = CreateService(store, database);

        await service.InspectAsync(new OrphanFileInspectionRequest(true, TimeSpan.FromDays(7)));
        var result = await service.InspectAsync(new OrphanFileInspectionRequest(false, TimeSpan.FromDays(7)));

        result.Eligible.Should().Be(1);
        result.Referenced.Should().Be(1);
        result.Retained.Should().Be(1);
        result.Deleted.Should().Be(0);
        store.DeleteAttempts.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_ShouldIsolateSingleFileFailureAndContinue()
    {
        var failing = Old("uploads/word-files/2026-01-01/failing.docx");
        var succeeding = Old("uploads/excel-files/2026-01-01/succeeding.xlsx");
        var store = new FakeStore([failing, succeeding])
        {
            Delete = path => path == failing.RelativePath
                ? throw new IOException("injected")
                : true
        };
        var service = CreateService(store, new FakeDatabaseReferences());

        await service.InspectAsync(new OrphanFileInspectionRequest(true, TimeSpan.FromDays(7)));
        var result = await service.InspectAsync(new OrphanFileInspectionRequest(false, TimeSpan.FromDays(7)));

        result.Eligible.Should().Be(2);
        result.Deleted.Should().Be(1);
        result.Retained.Should().Be(1);
        result.Failures.Should().Be(1);
        store.DeleteAttempts.Should().BeEquivalentTo([failing.RelativePath, succeeding.RelativePath]);
    }

    [Fact]
    public async Task Cancellation_ShouldStopBeforeDeletionAndPropagate()
    {
        using var cancellation = new CancellationTokenSource();
        var database = new FakeDatabaseReferences
        {
            Read = async token =>
            {
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return CompleteSnapshot();
            }
        };
        var store = new FakeStore([Old("uploads/word-files/2026-01-01/candidate.docx")]);
        var service = CreateService(store, database);

        Func<Task> inspect = async () => await service.InspectAsync(
            new OrphanFileInspectionRequest(false, TimeSpan.FromDays(7)),
            cancellation.Token);

        await inspect.Should().ThrowAsync<OperationCanceledException>();
        store.DeleteAttempts.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentRun_ShouldSkipSecondInspectionAndReleaseGateAfterCancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var database = new FakeDatabaseReferences
        {
            Read = async token =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
                return CompleteSnapshot();
            }
        };
        var coordinator = new OrphanFileInspectionCoordinator();
        var store = new FakeStore([]);
        var firstService = CreateService(store, database, coordinator);
        var secondService = CreateService(store, new FakeDatabaseReferences(), coordinator);

        var first = firstService.InspectAsync(new OrphanFileInspectionRequest(true, TimeSpan.FromDays(7)));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var skipped = await secondService.InspectAsync(new OrphanFileInspectionRequest(true, TimeSpan.FromDays(7)));
        skipped.SkippedBecauseAlreadyRunning.Should().BeTrue();

        release.TrySetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        var next = await secondService.InspectAsync(new OrphanFileInspectionRequest(true, TimeSpan.FromDays(7)));
        next.SkippedBecauseAlreadyRunning.Should().BeFalse();
    }

    [Fact]
    public async Task DatabaseReferenceFailure_ShouldFailClosedWithoutDeleteAttempts()
    {
        var files = new[]
        {
            Old("uploads/word-files/2026-01-01/one.docx"),
            Old("uploads/filled-files/2026-01-01/two.docx")
        };
        var store = new FakeStore(files);
        var database = new FakeDatabaseReferences
        {
            Read = _ => Task.FromException<OrphanReferenceSnapshot>(new IOException("database unavailable"))
        };
        var service = CreateService(store, database);

        var result = await service.InspectAsync(new OrphanFileInspectionRequest(false, TimeSpan.FromDays(7)));

        result.Scanned.Should().Be(2);
        result.Retained.Should().Be(2);
        result.Eligible.Should().Be(0);
        result.Deleted.Should().Be(0);
        result.Failures.Should().Be(1);
        store.DeleteAttempts.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelledScan_ShouldInvalidatePriorCandidateAndRequireTwoFreshCompletedRounds()
    {
        var candidate = Old("uploads/word-files/2026-01-01/candidate.docx");
        var store = new FakeStore([candidate]);
        var coordinator = new OrphanFileInspectionCoordinator();
        var completedService = CreateService(store, new FakeDatabaseReferences(), coordinator);
        await completedService.InspectAsync(new OrphanFileInspectionRequest(true, TimeSpan.FromDays(7)));

        using var cancellation = new CancellationTokenSource();
        var cancellingDatabase = new FakeDatabaseReferences
        {
            Read = async token =>
            {
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return CompleteSnapshot();
            }
        };
        var cancellingService = CreateService(store, cancellingDatabase, coordinator);
        Func<Task> cancelledRun = async () => await cancellingService.InspectAsync(
            new OrphanFileInspectionRequest(false, TimeSpan.FromDays(7)),
            cancellation.Token);
        await cancelledRun.Should().ThrowAsync<OperationCanceledException>();

        var firstFresh = await completedService.InspectAsync(
            new OrphanFileInspectionRequest(false, TimeSpan.FromDays(7)));
        firstFresh.Deleted.Should().Be(0, "中断轮次必须使上一轮候选证明失效");
        firstFresh.Retained.Should().Be(1);

        var secondFresh = await completedService.InspectAsync(
            new OrphanFileInspectionRequest(false, TimeSpan.FromDays(7)));
        secondFresh.Deleted.Should().Be(1);
    }

    [Fact]
    public async Task ManifestReferenceAppearingDuringDeleteRecheck_ShouldRetainCandidate()
    {
        var candidate = Old("uploads/filled-files/2026-01-01/raced.docx");
        var store = new FakeStore([candidate])
        {
            Probe = path => new OrphanReferenceProbe(
                path == candidate.RelativePath,
                true)
        };
        var service = CreateService(store, new FakeDatabaseReferences());
        await service.InspectAsync(new OrphanFileInspectionRequest(true, TimeSpan.FromDays(7)));

        var result = await service.InspectAsync(
            new OrphanFileInspectionRequest(false, TimeSpan.FromDays(7)));

        result.Eligible.Should().Be(1);
        result.Referenced.Should().Be(1);
        result.Deleted.Should().Be(0);
        result.Retained.Should().Be(1);
        store.DeleteAttempts.Should().BeEmpty();
    }

    [Fact]
    public async Task HostedService_StopAsync_ShouldCancelActiveInspectionAndNotStartAnotherRound()
    {
        var inspection = new BlockingInspectionAppService();
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IOrphanFileInspectionAppService>(inspection)
            .BuildServiceProvider();
        var options = new StaticOptionsMonitor<OrphanFileInspectionOptions>(new OrphanFileInspectionOptions
        {
            Enabled = true,
            ObservationMode = true,
            InitialDelaySeconds = 0,
            InspectionIntervalMinutes = 60,
            GracePeriodHours = 168
        });
        using var hostedService = new OrphanFileInspectionHostedService(
            services.GetRequiredService<IServiceScopeFactory>(),
            options,
            TimeProvider.System,
            NullLogger<OrphanFileInspectionHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);
        await inspection.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await hostedService.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        inspection.CancellationObserved.Should().BeTrue();
        inspection.InvocationCount.Should().Be(1);
    }

    [Theory]
    [InlineData("../outside.docx")]
    [InlineData("C:/outside.docx")]
    [InlineData("uploads/filled-files/manifests/task.json")]
    [InlineData("uploads/word-files/2026-01-01/file.tmp")]
    [InlineData("uploads/unknown/file.docx")]
    public void PathRules_ShouldRejectUnsafeOrNonContentPaths(string path)
    {
        OrphanFilePathRules.IsManagedContentPath(path).Should().BeFalse();
    }

    [Fact]
    public async Task FileStore_ShouldExcludeTemporaryAndManifestFilesAndRefuseChangedSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orphan-store-{Guid.NewGuid():N}");
        try
        {
            var fileStorage = CreateFileStorage(root);
            var contentPath = fileStorage.GetAbsolutePath("uploads/word-files/2026-01-01/content.docx");
            var temporaryPath = fileStorage.GetAbsolutePath("uploads/word-files/2026-01-01/content.tmp");
            var manifestPath = fileStorage.GetAbsolutePath("uploads/filled-files/manifests/task.json");
            Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            await File.WriteAllTextAsync(contentPath, "before");
            await File.WriteAllTextAsync(temporaryPath, "temporary");
            await File.WriteAllTextAsync(manifestPath, "{}");
            var store = new OrphanFileStore(fileStorage);

            var files = store.EnumerateManagedFiles();
            var snapshot = files.Should().ContainSingle().Subject;
            snapshot.RelativePath.Should().Be("uploads/word-files/2026-01-01/content.docx");

            await File.AppendAllTextAsync(contentPath, "-changed");
            (await store.DeleteIfUnchangedAsync(snapshot, CancellationToken.None)).Should().BeFalse();
            File.Exists(contentPath).Should().BeTrue();
            (await store.DeleteIfUnchangedAsync(
                new OrphanFileSnapshot("../outside.docx", snapshot.LastWriteTimeUtc, snapshot.Length),
                CancellationToken.None)).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task FileStore_ShouldMarkNamespaceIncompleteWhenManifestIsCorrupt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orphan-manifest-{Guid.NewGuid():N}");
        try
        {
            var fileStorage = CreateFileStorage(root);
            var manifestPath = fileStorage.GetAbsolutePath("uploads/filled-files/manifests/broken.json");
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            await File.WriteAllTextAsync(manifestPath, "{broken");
            var store = new OrphanFileStore(fileStorage);

            var snapshot = await store.ReadManifestReferencesAsync(CancellationToken.None);

            snapshot.FailureCount.Should().Be(1);
            snapshot.IsCompleteFor("uploads/filled-files/2026-01-01/file.docx").Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static OrphanFileInspectionAppService CreateService(
        FakeStore store,
        FakeDatabaseReferences database,
        OrphanFileInspectionCoordinator? coordinator = null)
    {
        return new OrphanFileInspectionAppService(
            store,
            database,
            coordinator ?? new OrphanFileInspectionCoordinator(),
            new FixedTimeProvider(Now),
            NullLogger<OrphanFileInspectionAppService>.Instance);
    }

    private static OrphanFileSnapshot Old(string path) => new(path, Now.AddDays(-8), 10);

    private static OrphanFileSnapshot New(string path) => new(path, Now.AddHours(-1), 10);

    private static OrphanReferenceSnapshot CompleteSnapshot(params string[] paths) => new(
        new HashSet<string>(paths.Select(OrphanFilePathRules.Normalize), StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        0);

    private static FileStorageService CreateFileStorage(string root)
    {
        Directory.CreateDirectory(root);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:BasePath"] = root
            })
            .Build();
        return new FileStorageService(new TestWebHostEnvironment(root), configuration);
    }

    private sealed class FakeStore(IReadOnlyList<OrphanFileSnapshot> files) : IOrphanFileStore
    {
        public OrphanReferenceSnapshot ManifestSnapshot { get; set; } = CompleteSnapshot();
        public Func<string, OrphanReferenceProbe> Probe { get; set; } = _ => new OrphanReferenceProbe(false, true);
        public Func<string, bool> Delete { get; set; } = _ => true;
        public List<string> DeleteAttempts { get; } = [];

        public IReadOnlyList<OrphanFileSnapshot> EnumerateManagedFiles() => files;

        public Task<OrphanReferenceSnapshot> ReadManifestReferencesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ManifestSnapshot);

        public Task<OrphanReferenceProbe> ProbeManifestReferenceAsync(
            string relativePath,
            CancellationToken cancellationToken) => Task.FromResult(Probe(relativePath));

        public Task<bool> DeleteIfUnchangedAsync(
            OrphanFileSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            DeleteAttempts.Add(snapshot.RelativePath);
            return Task.FromResult(Delete(snapshot.RelativePath));
        }
    }

    private sealed class FakeDatabaseReferences : IOrphanDatabaseReferenceQuery
    {
        public OrphanReferenceSnapshot Snapshot { get; set; } = CompleteSnapshot();
        public Func<CancellationToken, Task<OrphanReferenceSnapshot>>? Read { get; set; }
        public Func<string, OrphanReferenceProbe> Probe { get; set; } = _ => new OrphanReferenceProbe(false, true);

        public Task<OrphanReferenceSnapshot> ReadReferencesAsync(CancellationToken cancellationToken) =>
            Read?.Invoke(cancellationToken) ?? Task.FromResult(Snapshot);

        public Task<OrphanReferenceProbe> ProbeReferenceAsync(
            string relativePath,
            CancellationToken cancellationToken) => Task.FromResult(Probe(relativePath));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class BlockingInspectionAppService : IOrphanFileInspectionAppService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }
        public int InvocationCount { get; private set; }

        public async Task<OrphanFileInspectionResult> InspectAsync(
            OrphanFileInspectionRequest request,
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

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "AcceptanceSpecSystem.Api.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}
