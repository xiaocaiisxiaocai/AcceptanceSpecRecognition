using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class FileCompareResourceBudgetTests
{
    [Fact]
    public async Task Compare_WhenLegacyContentNeedsTemporaryFiles_ShouldDeleteArtifactsAfterSuccess()
    {
        using var governor = CreateGovernor();
        var temporaryStorage = new MemoryTemporaryStorage();
        var service = new FileCompareService(
            new DocumentServiceFactory(), new MissingFileStorage(), governor, temporaryStorage);
        var content = CreateWordDocxBytes("same");
        var first = new WordFile { FileType = UploadedFileType.WordDocx, FileContent = content };
        var second = new WordFile { FileType = UploadedFileType.WordDocx, FileContent = content };

        var result = await service.CompareAsync(first, second);

        result.Items.Should().ContainSingle(item => item.DiffType == FileCompareDiffType.Unchanged);
        temporaryStorage.StageCalls.Should().Be(2);
        temporaryStorage.ActiveLeases.Should().Be(0);
    }

    [Fact]
    public async Task Compare_WhenAlreadyCancelled_ShouldNotLeaveMaterializedTemporaryFiles()
    {
        using var governor = CreateGovernor();
        var temporaryStorage = new MemoryTemporaryStorage();
        var service = new FileCompareService(
            new DocumentServiceFactory(), new MissingFileStorage(), governor, temporaryStorage);
        var content = CreateWordDocxBytes("cancelled");
        var file = new WordFile { FileType = UploadedFileType.WordDocx, FileContent = content };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var compareTask = service.CompareAsync(file, file, cancellation.Token);
        Func<Task> compare = async () => await compareTask;

        await compare.Should().ThrowAsync<OperationCanceledException>();
        temporaryStorage.ActiveLeases.Should().Be(0);
    }

    [Fact]
    public async Task WordCompare_节点精确达到上限允许且下一节点返回422()
    {
        var content = CreateWordDocxBytes("one", "two");
        var file = new WordFile { FileType = UploadedFileType.WordDocx, FileContent = content };
        using var allowedGovernor = CreateGovernor(maxCells: 4);
        var allowed = new FileCompareService(new DocumentServiceFactory(), new MissingFileStorage(), allowedGovernor, new MemoryTemporaryStorage());
        (await allowed.CompareAsync(file, file)).Items.Should().HaveCount(2);

        using var rejectedGovernor = CreateGovernor(maxCells: 3);
        var rejected = new FileCompareService(new DocumentServiceFactory(), new MissingFileStorage(), rejectedGovernor, new MemoryTemporaryStorage());
        Func<Task> compare = async () => await rejected.CompareAsync(file, file);

        await compare.Should().ThrowAsync<FileCompareBudgetExceededException>()
            .Where(exception => exception.Code == 422 && exception.BudgetName == "file_compare_cells");
    }

    [Fact]
    public async Task WordCompare_空段落也应计入节点预算()
    {
        var content = CreateWordDocxBytes("visible", string.Empty);
        var file = new WordFile { FileType = UploadedFileType.WordDocx, FileContent = content };
        using var governor = CreateGovernor(maxCells: 3);
        var service = new FileCompareService(new DocumentServiceFactory(), new MissingFileStorage(), governor, new MemoryTemporaryStorage());

        Func<Task> compare = async () => await service.CompareAsync(file, file);

        await compare.Should().ThrowAsync<FileCompareBudgetExceededException>()
            .Where(exception => exception.BudgetName == "file_compare_cells");
    }

    [Fact]
    public async Task WordCompare_单段大量Run应在段落内部响应取消()
    {
        using var cancellation = new CancellationTokenSource();
        var governor = new CancelAfterFirstNodeGovernor(cancellation);
        var service = new FileCompareService(new DocumentServiceFactory(), new MissingFileStorage(), governor, new MemoryTemporaryStorage());
        var content = CreateWordWithRuns(2_000);
        var file = new WordFile { FileType = UploadedFileType.WordDocx, FileContent = content };

        Func<Task> compare = async () => await service.CompareAsync(file, file, cancellation.Token);

        await compare.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WordCompare_修改项只计一条且超出差异上限返回422()
    {
        var first = new WordFile
        {
            FileType = UploadedFileType.WordDocx,
            FileContent = CreateWordDocxBytes("old")
        };
        var second = new WordFile
        {
            FileType = UploadedFileType.WordDocx,
            FileContent = CreateWordDocxBytes("new")
        };
        using var allowedGovernor = CreateGovernor(maxDiffs: 1);
        var allowed = new FileCompareService(new DocumentServiceFactory(), new MissingFileStorage(), allowedGovernor, new MemoryTemporaryStorage());
        (await allowed.CompareAsync(first, second)).Items.Should()
            .ContainSingle(item => item.DiffType == FileCompareDiffType.Modified);

        second.FileContent = CreateWordDocxBytes("new", "added");
        using var rejectedGovernor = CreateGovernor(maxDiffs: 1);
        var rejected = new FileCompareService(new DocumentServiceFactory(), new MissingFileStorage(), rejectedGovernor, new MemoryTemporaryStorage());
        Func<Task> compare = async () => await rejected.CompareAsync(first, second);

        await compare.Should().ThrowAsync<FileCompareBudgetExceededException>()
            .Where(exception => exception.BudgetName == "file_compare_diff_items");
    }

    [Fact]
    public async Task WordCompare_ChangedOp下界超限应在最终投影前拒绝()
    {
        var first = new WordFile
        {
            FileType = UploadedFileType.WordDocx,
            FileContent = CreateWordDocxBytes("A", "B", "C")
        };
        var second = new WordFile
        {
            FileType = UploadedFileType.WordDocx,
            FileContent = CreateWordDocxBytes("X", "Y", "Z")
        };
        using var governor = CreateGovernor(maxDiffs: 1);
        var service = new FileCompareService(new DocumentServiceFactory(), new MissingFileStorage(), governor, new MemoryTemporaryStorage());

        var exception = await FluentActions.Awaiting(() => service.CompareAsync(first, second))
            .Should().ThrowAsync<FileCompareBudgetExceededException>();

        exception.Which.StackTrace.Should().Contain("AppendDiffOp");
    }

    [Fact]
    public async Task WordCompare_Chunk操作生成中应响应取消()
    {
        using var cancellation = new CancellationTokenSource();
        var governor = new CancelAfterFirstDiffGovernor(cancellation);
        var service = new FileCompareService(new DocumentServiceFactory(), new MissingFileStorage(), governor, new MemoryTemporaryStorage());
        var first = new WordFile
        {
            FileType = UploadedFileType.WordDocx,
            FileContent = CreateWordDocxBytes(Enumerable.Range(0, 600).Select(index => $"A{index}").ToArray())
        };
        var second = new WordFile
        {
            FileType = UploadedFileType.WordDocx,
            FileContent = CreateWordDocxBytes(Enumerable.Range(0, 600).Select(index => $"B{index}").ToArray())
        };

        Func<Task> compare = async () => await service.CompareAsync(first, second, cancellation.Token);

        await compare.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExcelCompare_Metadata预测超限应在TableData物化前拒绝()
    {
        var parser = new RecordingExcelParser(
        [
            new TableInfo
            {
                Index = 0,
                RowCount = 2,
                ColumnCount = 2,
                Headers = ["H1", "H2"]
            }
        ]);
        using var governor = CreateGovernor(maxCells: 8);
        var service = new FileCompareService(
            new DocumentServiceFactory(),
            new MissingFileStorage(),
            governor,
            new MemoryTemporaryStorage(),
            parser);
        var file = new WordFile
        {
            FileType = UploadedFileType.ExcelXlsx,
            FileContent = [1]
        };

        Func<Task> compare = async () => await service.CompareAsync(file, file);

        await compare.Should().ThrowAsync<FileCompareBudgetExceededException>()
            .Where(exception => exception.BudgetName == "file_compare_cells");
        parser.ExtractCalls.Should().Be(0, "metadata 已证明超限时不得物化 TableData");
    }

    [Fact]
    public async Task ExcelCompare_Metadata未超限但实际节点漂移时应在读取阶段拒绝()
    {
        var parser = new RecordingExcelParser(
            [new TableInfo { Index = 0, RowCount = 1, ColumnCount = 1, Headers = ["H"] }],
            new TableData
            {
                Headers = ["H"],
                Rows =
                [
                    new RowData
                    {
                        Index = 0,
                        Cells =
                        [
                            new CellData { ColumnIndex = 0, Value = "V1" },
                            new CellData { ColumnIndex = 1, Value = "V2" }
                        ]
                    }
                ]
            });
        using var governor = CreateGovernor(maxCells: 4);
        var service = new FileCompareService(
            new DocumentServiceFactory(),
            new MissingFileStorage(),
            governor,
            new MemoryTemporaryStorage(),
            parser);
        var file = new WordFile { FileType = UploadedFileType.ExcelXlsx, FileContent = [1] };

        Func<Task> compare = async () => await service.CompareAsync(file, file);

        await compare.Should().ThrowAsync<FileCompareBudgetExceededException>()
            .Where(exception => exception.BudgetName == "file_compare_cells");
        parser.ExtractCalls.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(20_001, 1)]
    [InlineData(1, 101)]
    public async Task ExcelCompare_Sheet维度超限应返回文件比较专用422(int rows, int columns)
    {
        var parser = new RecordingExcelParser(
            [new TableInfo { Index = 0, RowCount = rows, ColumnCount = columns }]);
        using var governor = CreateGovernor();
        var service = new FileCompareService(
            new DocumentServiceFactory(),
            new MissingFileStorage(),
            governor,
            new MemoryTemporaryStorage(),
            parser);
        var file = new WordFile { FileType = UploadedFileType.ExcelXlsx, FileContent = [1] };

        Func<Task> compare = async () => await service.CompareAsync(file, file);

        await compare.Should().ThrowAsync<FileCompareBudgetExceededException>()
            .Where(exception => exception.Code == 422);
        parser.ExtractCalls.Should().Be(0);
    }

    private static ResourceBudgetGovernor CreateGovernor(
        long maxCells = 1_000_000,
        long maxDiffs = 100_000) => new(Microsoft.Extensions.Options.Options.Create(new ResourceBudgetOptions
    {
        MaxConcurrentDocumentParsers = 1,
        MaxDocumentBytes = 2 * 1024 * 1024,
        MaxFileCompareCells = maxCells,
        MaxFileCompareDiffItems = maxDiffs
    }));

    private static byte[] CreateWordDocxBytes(params string[] texts)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(
                texts.Select(text => new Paragraph(new Run(new Text(text))))));
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static byte[] CreateWordWithRuns(int runCount)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var paragraph = new Paragraph();
            for (var index = 0; index < runCount; index++)
                paragraph.Append(new Run(new Text("x")));
            main.Document = new Document(new Body(paragraph));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private sealed class MissingFileStorage : IFileStorageService
    {
        public Task<string> SaveUploadedWordAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<string> SaveUploadedExcelAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<string> SaveFilledWordAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<string> SaveSmartFillPlaybackArchiveAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<string> SaveSmartFillResultArchiveAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Stream OpenReadStream(string relativePath) => throw new FileNotFoundException();
        public Task<string> WriteHealthCheckFileAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public string GetAbsolutePath(string relativePath) => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public Task DeleteIfExistsAsync(string? relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingExcelParser(
        IReadOnlyList<TableInfo> tables,
        TableData? extracted = null) : IFileCompareDocumentParser
    {
        public int ExtractCalls { get; private set; }

        public Task<IReadOnlyList<TableInfo>> GetTablesAsync(
            Stream content,
            CancellationToken cancellationToken) =>
            Task.FromResult(tables);

        public Task<TableData> ExtractTableDataAsync(
            Stream content,
            int tableIndex,
            ColumnMapping mapping,
            int maxDataRowCount,
            CancellationToken cancellationToken)
        {
            ExtractCalls++;
            return Task.FromResult(extracted ?? new TableData());
        }
    }

    private sealed class CancelAfterFirstNodeGovernor(CancellationTokenSource cancellation)
        : IResourceBudgetGovernor
    {
        public ValueTask<ResourceBudgetLease> AcquireAsync(
            ResourceWorkload workload,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public void ValidateDocumentSize(long bytes) { }
        public void ValidateWriteOperations(int operationCount) { }
        public void ValidateMatchingItems(int itemCount) { }
        public void ValidateDuplicateCandidates(int candidateCount) { }
        public void ValidateDuplicateComparisons(long comparisonCount) { }
        public void ValidateFileCompareCells(long cellCount) => cancellation.Cancel();
        public void ValidateFileCompareDiffItems(long diffItemCount) { }
        public void ValidateFileCompareResultBytes(long bytes) { }
    }

    private sealed class CancelAfterFirstDiffGovernor(CancellationTokenSource cancellation)
        : IResourceBudgetGovernor
    {
        public ValueTask<ResourceBudgetLease> AcquireAsync(
            ResourceWorkload workload,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public void ValidateDocumentSize(long bytes) { }
        public void ValidateWriteOperations(int operationCount) { }
        public void ValidateMatchingItems(int itemCount) { }
        public void ValidateDuplicateCandidates(int candidateCount) { }
        public void ValidateDuplicateComparisons(long comparisonCount) { }
        public void ValidateFileCompareCells(long cellCount) { }
        public void ValidateFileCompareDiffItems(long diffItemCount) => cancellation.Cancel();
        public void ValidateFileCompareResultBytes(long bytes) { }
    }

    private sealed class MemoryTemporaryStorage : IFileCompareTemporaryStorage
    {
        public int StageCalls { get; private set; }
        public int ActiveLeases { get; private set; }

        public async Task<TemporaryFileLease> StageUploadAsync(
            Stream content,
            long maxBytes,
            CancellationToken cancellationToken = default)
        {
            StageCalls++;
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length > maxBytes)
                throw new ApplicationServiceException(413, "文件过大");
            ActiveLeases++;
            return new MemoryLease(
                buffer.ToArray(),
                () => ActiveLeases--);
        }

        public Task<TemporaryFileLease> CreateOutputAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<TemporaryFileLease>(new MemoryLease([], () => { }));

        public Task CleanupExpiredAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        private sealed class MemoryLease(byte[] content, Action released) : TemporaryFileLease
        {
            private readonly byte[] _content = content;
            private int _disposed;
            public override long Length => _content.LongLength;
            public override string Sha256 => Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(_content)).ToLowerInvariant();
            public override Stream OpenRead() => new MemoryStream(_content, writable: false);
            public override Stream OpenWrite() => new MemoryStream();
            public override ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    released();
                return ValueTask.CompletedTask;
            }
        }
    }
}
