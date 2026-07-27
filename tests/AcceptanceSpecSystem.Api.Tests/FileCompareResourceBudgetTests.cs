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
        var before = FindCompareTemporaryFiles();
        using var governor = CreateGovernor();
        var service = new FileCompareService(new DocumentServiceFactory(), new MissingFileStorage(), governor);
        var content = CreateWordDocxBytes("same");
        var first = new WordFile { FileType = UploadedFileType.WordDocx, FileContent = content };
        var second = new WordFile { FileType = UploadedFileType.WordDocx, FileContent = content };

        var result = await service.CompareAsync(first, second);

        result.Items.Should().ContainSingle(item => item.DiffType == FileCompareDiffType.Unchanged);
        FindCompareTemporaryFiles().Should().BeEquivalentTo(before,
            "文件对比完成后必须删除为旧记录创建的临时文件");
    }

    [Fact]
    public async Task Compare_WhenAlreadyCancelled_ShouldNotLeaveMaterializedTemporaryFiles()
    {
        var before = FindCompareTemporaryFiles();
        using var governor = CreateGovernor();
        var service = new FileCompareService(new DocumentServiceFactory(), new MissingFileStorage(), governor);
        var content = CreateWordDocxBytes("cancelled");
        var file = new WordFile { FileType = UploadedFileType.WordDocx, FileContent = content };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var compareTask = service.CompareAsync(file, file, cancellation.Token);
        Func<Task> compare = async () => await compareTask;

        await compare.Should().ThrowAsync<OperationCanceledException>();
        FindCompareTemporaryFiles().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task WordCompare_节点精确达到上限允许且下一节点返回422()
    {
        var content = CreateWordDocxBytes("one", "two");
        var file = new WordFile { FileType = UploadedFileType.WordDocx, FileContent = content };
        using var allowedGovernor = CreateGovernor(maxCells: 4);
        var allowed = new FileCompareService(new DocumentServiceFactory(), new MissingFileStorage(), allowedGovernor);
        (await allowed.CompareAsync(file, file)).Items.Should().HaveCount(2);

        using var rejectedGovernor = CreateGovernor(maxCells: 3);
        var rejected = new FileCompareService(new DocumentServiceFactory(), new MissingFileStorage(), rejectedGovernor);
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
        var service = new FileCompareService(new DocumentServiceFactory(), new MissingFileStorage(), governor);

        Func<Task> compare = async () => await service.CompareAsync(file, file);

        await compare.Should().ThrowAsync<FileCompareBudgetExceededException>()
            .Where(exception => exception.BudgetName == "file_compare_cells");
    }

    [Fact]
    public async Task WordCompare_单段大量Run应在段落内部响应取消()
    {
        using var cancellation = new CancellationTokenSource();
        var governor = new CancelAfterFirstNodeGovernor(cancellation);
        var service = new FileCompareService(new DocumentServiceFactory(), new MissingFileStorage(), governor);
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
        var allowed = new FileCompareService(new DocumentServiceFactory(), new MissingFileStorage(), allowedGovernor);
        (await allowed.CompareAsync(first, second)).Items.Should()
            .ContainSingle(item => item.DiffType == FileCompareDiffType.Modified);

        second.FileContent = CreateWordDocxBytes("new", "added");
        using var rejectedGovernor = CreateGovernor(maxDiffs: 1);
        var rejected = new FileCompareService(new DocumentServiceFactory(), new MissingFileStorage(), rejectedGovernor);
        Func<Task> compare = async () => await rejected.CompareAsync(first, second);

        await compare.Should().ThrowAsync<FileCompareBudgetExceededException>()
            .Where(exception => exception.BudgetName == "file_compare_diff_items");
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
        using var governor = CreateGovernor(maxCells: 7);
        var service = new FileCompareService(
            new DocumentServiceFactory(),
            new MissingFileStorage(),
            governor,
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

    private static ResourceBudgetGovernor CreateGovernor(
        long maxCells = 1_000_000,
        long maxDiffs = 100_000) => new(Microsoft.Extensions.Options.Options.Create(new ResourceBudgetOptions
    {
        MaxConcurrentDocumentParsers = 1,
        MaxDocumentBytes = 2 * 1024 * 1024,
        MaxFileCompareCells = maxCells,
        MaxFileCompareDiffItems = maxDiffs
    }));

    private static string[] FindCompareTemporaryFiles() => Directory
        .GetFiles(Path.GetTempPath(), "acceptance-file-compare-*")
        .OrderBy(path => path, StringComparer.Ordinal)
        .ToArray();

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
        public Stream OpenReadStream(string relativePath) => throw new FileNotFoundException();
        public Task<string> WriteHealthCheckFileAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public string GetAbsolutePath(string relativePath) => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public Task DeleteIfExistsAsync(string? relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingExcelParser(IReadOnlyList<TableInfo> tables) : IFileCompareDocumentParser
    {
        public int ExtractCalls { get; private set; }

        public Task<IReadOnlyList<TableInfo>> GetTablesAsync(
            string filePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(tables);

        public Task<TableData> ExtractTableDataAsync(
            string filePath,
            int tableIndex,
            ColumnMapping mapping,
            int maxDataRowCount,
            CancellationToken cancellationToken)
        {
            ExtractCalls++;
            return Task.FromResult(new TableData());
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
}
