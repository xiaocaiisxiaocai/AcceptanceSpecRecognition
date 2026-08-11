using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Application.Options;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Reflection;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class UploadedDocumentSnapshotServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_ShouldReuseParsedSnapshotAcrossRequests()
    {
        var (root, _, wordFile) = await CreateFixtureAsync();
        var service = CreateService(root);

        var first = await service.GetSnapshotAsync(wordFile);
        var second = await service.GetSnapshotAsync(wordFile);

        service.ParseInvocationCount.Should().Be(1);
        first.Tables.Should().HaveCount(1);
        second.Tables[0].Name.Should().Be(first.Tables[0].Name);
        second.TableData[0].Rows[0].GetValue(0).Should().Be("外观");
    }

    [Fact]
    public async Task GetSnapshotAsync_ShouldReparseAfterInvalidate()
    {
        var (root, _, wordFile) = await CreateFixtureAsync();
        var service = CreateService(root);

        await service.GetSnapshotAsync(wordFile);
        service.Invalidate(wordFile.Id);
        await service.GetSnapshotAsync(wordFile);

        service.ParseInvocationCount.Should().Be(2);
    }

    [Fact]
    public async Task GetSnapshotAsync_ShouldReturnIsolatedCopies()
    {
        var (root, _, wordFile) = await CreateFixtureAsync();
        var service = CreateService(root);

        var first = await service.GetSnapshotAsync(wordFile);
        var second = await service.GetSnapshotAsync(wordFile);
        second.TableData[0].Rows[0].Cells[0].Value = "changed";

        first.TableData[0].Rows[0].Cells[0].Value.Should().Be("外观");
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenDisabled_ShouldParseOnEveryRequest()
    {
        var (root, _, wordFile) = await CreateFixtureAsync();
        var service = CreateService(root, enabled: false);

        await service.GetSnapshotAsync(wordFile);
        await service.GetSnapshotAsync(wordFile);

        service.ParseInvocationCount.Should().Be(2);
    }

    [Fact]
    public async Task GetSnapshotAsync_ConcurrentMiss_ShouldSingleFlightParse()
    {
        var (root, _, wordFile) = await CreateFixtureAsync();
        var service = CreateService(root);

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => service.GetSnapshotAsync(wordFile))
            .ToArray();
        var snapshots = await Task.WhenAll(tasks);

        service.ParseInvocationCount.Should().Be(1);
        snapshots.Should().AllSatisfy(snapshot =>
            snapshot.TableData[0].Rows[0].GetValue(0).Should().Be("外观"));
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenOneWaiterCancelled_ShouldNotPoisonCacheForOthers()
    {
        var (root, _, wordFile) = await CreateFixtureAsync();
        using var gate = new BlockingResourceGovernor();
        var service = CreateService(root, governor: gate);
        using var cts = new CancellationTokenSource();

        var cancelledTask = service.GetSnapshotAsync(wordFile, cts.Token);
        await gate.WaitUntilBlockedAsync();
        cts.Cancel();

        await cancelledTask.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();

        gate.Release();
        var snapshot = await service.GetSnapshotAsync(wordFile);
        snapshot.Tables.Should().HaveCount(1);
        service.ParseInvocationCount.Should().Be(1);

        await service.GetSnapshotAsync(wordFile);
        service.ParseInvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenParseFails_ShouldNotCacheAndAllowRetry()
    {
        var (root, relativePath, wordFile) = await CreateFixtureAsync();
        var service = CreateService(root);
        wordFile.FilePath = "uploads/excel-files/2026-08-11/missing.xlsx";

        await service.Invoking(s => s.GetSnapshotAsync(wordFile))
            .Should()
            .ThrowAsync<ApplicationServiceException>();

        wordFile.FilePath = relativePath;
        var snapshot = await service.GetSnapshotAsync(wordFile);

        snapshot.Tables.Should().HaveCount(1);
        service.ParseInvocationCount.Should().Be(2);
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenFileHashChanges_ShouldReparse()
    {
        var (root, _, wordFile) = await CreateFixtureAsync();
        var service = CreateService(root);

        await service.GetSnapshotAsync(wordFile);
        wordFile.FileHash = "changed-hash";
        await service.GetSnapshotAsync(wordFile);

        service.ParseInvocationCount.Should().Be(2);
    }

    [Fact]
    public async Task GetSnapshotAsync_ForTemporaryFilesWithoutHash_ShouldUsePathToAvoidCacheCollision()
    {
        var (root, _, firstFile) = await CreateFixtureAsync();
        var secondRelativePath = "uploads/excel-files/2026-08-11/second-file.xlsx";
        var secondAbsolutePath = Path.Combine(root, secondRelativePath.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllBytesAsync(secondAbsolutePath, CreateWorkbookBytes("尺寸"));
        var secondFile = new WordFile
        {
            Id = 0,
            FileName = "second-file.xlsx",
            FileType = UploadedFileType.ExcelXlsx,
            FilePath = secondRelativePath,
            FileHash = string.Empty
        };
        firstFile.Id = 0;
        firstFile.FileHash = string.Empty;
        var service = CreateService(root);

        var first = await service.GetSnapshotAsync(firstFile);
        var second = await service.GetSnapshotAsync(secondFile);

        service.ParseInvocationCount.Should().Be(2);
        first.TableData[0].Rows[0].GetValue(0).Should().Be("外观");
        second.TableData[0].Rows[0].GetValue(0).Should().Be("尺寸");
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenEntryTooLarge_ShouldReturnWithoutCaching()
    {
        var (root, _, wordFile) = await CreateFixtureAsync();
        var service = CreateService(root, maxEntryBytes: 1);

        await service.GetSnapshotAsync(wordFile);
        await service.GetSnapshotAsync(wordFile);

        service.ParseInvocationCount.Should().Be(2);
    }

    [Fact]
    public async Task Invalidate_DuringInflightParse_ShouldPreventOldSnapshotFromReenteringCache()
    {
        var (root, _, wordFile) = await CreateFixtureAsync();
        using var gate = new BlockingResourceGovernor();
        var service = CreateService(root, governor: gate);

        var firstRequest = service.GetSnapshotAsync(wordFile);
        await gate.WaitUntilBlockedAsync();
        service.Invalidate(wordFile.Id);
        gate.Release();
        await firstRequest;

        var secondRequest = service.GetSnapshotAsync(wordFile);
        gate.Release();
        await secondRequest;

        service.ParseInvocationCount.Should().Be(2);
    }

    [Fact]
    public async Task CapacityEviction_ShouldRemoveHistoricalKeysFromFileIndex()
    {
        var (root, _, wordFile) = await CreateFixtureAsync();
        var service = CreateService(
            root,
            totalBudgetBytes: 1024,
            minEntryChargeBytes: 1024);

        for (var index = 0; index < 12; index++)
        {
            wordFile.FileHash = $"hash-{index}";
            await service.GetSnapshotAsync(wordFile);
        }

        await WaitUntilAsync(() => GetTrackedKeyCount(service) <= 1);

        GetTrackedKeyCount(service).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task ConcurrentMiss_ShouldLogOneMissAndOneShared()
    {
        var (root, _, wordFile) = await CreateFixtureAsync();
        using var gate = new BlockingResourceGovernor();
        var logger = new CollectingLogger<UploadedDocumentSnapshotService>();
        var service = CreateService(root, governor: gate, logger: logger);

        var owner = service.GetSnapshotAsync(wordFile);
        await gate.WaitUntilBlockedAsync();
        var waiter = service.GetSnapshotAsync(wordFile);
        gate.Release();
        await Task.WhenAll(owner, waiter);

        logger.Messages.Count(message => message.Contains("Outcome=miss", StringComparison.Ordinal))
            .Should().Be(1);
        logger.Messages.Count(message => message.Contains("Outcome=shared", StringComparison.Ordinal))
            .Should().Be(1);
    }

    internal static UploadedDocumentSnapshotService CreateService(
        string root,
        bool enabled = true,
        long maxEntryBytes = 64L * 1024 * 1024,
        IResourceBudgetGovernor? governor = null,
        long totalBudgetBytes = 128L * 1024 * 1024,
        long minEntryChargeBytes = 1024,
        ILogger<UploadedDocumentSnapshotService>? logger = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new UploadedDocumentSnapshotOptions
        {
            Enabled = enabled,
            SlidingExpirationSeconds = 120,
            TotalBudgetBytes = totalBudgetBytes,
            MaxEntryBytes = maxEntryBytes,
            MinEntryChargeBytes = minEntryChargeBytes
        });
        return new UploadedDocumentSnapshotService(
            new DocumentServiceFactory(),
            new TestPathResolver(root),
            governor ?? new ResourceBudgetGovernor(Microsoft.Extensions.Options.Options.Create(new ResourceBudgetOptions
            {
                MaxConcurrentDocumentParsers = 4,
                MaxConcurrentDocumentWriters = 2,
                MaxConcurrentHighCostMatching = 2,
                MaxDocumentBytes = 50L * 1024 * 1024,
                MaxWriteOperations = 1000,
                MaxMatchingItems = 10000,
                MaxDuplicateCandidates = 1000,
                MaxDuplicatePairComparisons = 10000,
                MaxFileCompareCells = 100000,
                MaxFileCompareDiffItems = 10000,
                MaxFileCompareResultBytes = 10L * 1024 * 1024
            })),
            new TestHostApplicationLifetime(),
            options,
            logger ?? NullLogger<UploadedDocumentSnapshotService>.Instance);
    }

    private static int GetTrackedKeyCount(UploadedDocumentSnapshotService service)
    {
        var field = typeof(UploadedDocumentSnapshotService).GetField(
            "_fileIndex",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var index = (ConcurrentDictionary<int, ConcurrentDictionary<string, byte>>)field!.GetValue(service)!;
        return index.Values.Sum(keys => keys.Count);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }
    }

    private sealed class BlockingResourceGovernor : IResourceBudgetGovernor, IDisposable
    {
        private readonly SemaphoreSlim _block = new(0, 1);
        private int _blockedCount;

        public async Task WaitUntilBlockedAsync()
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                if (Volatile.Read(ref _blockedCount) > 0)
                {
                    return;
                }

                await Task.Delay(10);
            }

            throw new TimeoutException("解析资源闸门未进入阻塞状态。");
        }

        public void Release() => _block.Release();

        public async ValueTask<ResourceBudgetLease> AcquireAsync(
            ResourceWorkload workload,
            CancellationToken cancellationToken = default)
        {
            if (workload == ResourceWorkload.DocumentParsing)
            {
                Interlocked.Increment(ref _blockedCount);
                await _block.WaitAsync(cancellationToken);
            }

            return new ResourceBudgetLease(null, TimeSpan.Zero);
        }

        public void ValidateDocumentSize(long bytes)
        {
        }

        public void ValidateWriteOperations(int operationCount)
        {
        }

        public void ValidateMatchingItems(int itemCount)
        {
        }

        public void ValidateDuplicateCandidates(int candidateCount)
        {
        }

        public void ValidateDuplicateComparisons(long comparisonCount)
        {
        }

        public void Dispose() => _block.Dispose();
    }

    private static async Task<(string Root, string RelativePath, WordFile WordFile)> CreateFixtureAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "uploaded-snapshot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var relativePath = "uploads/excel-files/2026-08-11/test-file.xlsx";
        var absolutePath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await File.WriteAllBytesAsync(absolutePath, CreateWorkbookBytes());
        var hash = FileStorageService.ComputeSha256(await File.ReadAllBytesAsync(absolutePath));
        var wordFile = new WordFile
        {
            Id = 42,
            FileName = "test-file.xlsx",
            FileType = UploadedFileType.ExcelXlsx,
            FilePath = relativePath,
            FileHash = hash
        };
        return (root, relativePath, wordFile);
    }

    private static byte[] CreateWorkbookBytes(string project = "外观")
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("测试表");
        sheet.Cell(1, 1).Value = "项目";
        sheet.Cell(1, 2).Value = "规格";
        sheet.Cell(2, 1).Value = project;
        sheet.Cell(2, 2).Value = "无划伤";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private sealed class TestPathResolver(string root) : IUploadedDocumentPathResolver
    {
        public string ResolveAbsolutePath(string relativePath) =>
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted { get; } = CancellationToken.None;
        public CancellationToken ApplicationStopping { get; } = CancellationToken.None;
        public CancellationToken ApplicationStopped { get; } = CancellationToken.None;

        public void StopApplication()
        {
        }
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
