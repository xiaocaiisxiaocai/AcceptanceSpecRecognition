using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Documents;
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
    public async Task Compare_WhenCancelledWhileWaitingForParser_ShouldDeleteMaterializedTemporaryFiles()
    {
        var before = FindCompareTemporaryFiles();
        using var governor = CreateGovernor();
        using var occupiedParser = await governor.AcquireAsync(ResourceWorkload.DocumentParsing);
        var service = new FileCompareService(new DocumentServiceFactory(), new MissingFileStorage(), governor);
        var content = CreateWordDocxBytes("cancelled");
        var file = new WordFile { FileType = UploadedFileType.WordDocx, FileContent = content };
        using var cancellation = new CancellationTokenSource();
        var compareTask = service.CompareAsync(file, file, cancellation.Token);
        await WaitUntilAsync(() => FindCompareTemporaryFiles().Except(before).Count() >= 2);
        cancellation.Cancel();
        Func<Task> compare = async () => await compareTask;

        await compare.Should().ThrowAsync<OperationCanceledException>();
        FindCompareTemporaryFiles().Should().BeEquivalentTo(before);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private static ResourceBudgetGovernor CreateGovernor() => new(Microsoft.Extensions.Options.Options.Create(new ResourceBudgetOptions
    {
        MaxConcurrentDocumentParsers = 1,
        MaxDocumentBytes = 2 * 1024 * 1024
    }));

    private static string[] FindCompareTemporaryFiles() => Directory
        .GetFiles(Path.GetTempPath(), "acceptance-file-compare-*")
        .OrderBy(path => path, StringComparer.Ordinal)
        .ToArray();

    private static byte[] CreateWordDocxBytes(string text)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(new Text(text)))));
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
}
