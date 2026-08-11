using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Application.Options;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class UploadedDocumentSnapshotCrossScopeTests
{
    [Fact]
    public async Task DocumentTableAccessService_ShouldReuseSnapshotAcrossMetadataPreviewAndExtraction()
    {
        var harness = await SnapshotTestHarness.CreateAsync();
        var service = harness.CreateTableAccessService();
        var mapping = new ColumnMapping
        {
            HeaderRowIndex = 0,
            HeaderRowCount = 1,
            DataStartRowIndex = 1
        };

        await service.GetTablesAsync(harness.WordFile);
        await service.ExtractTableDataAsync(harness.WordFile, 0, mapping);
        var items = await service.ExtractMatchSourceItemsAsync(harness.WordFile, 0, 0, 1);

        harness.SnapshotService.ParseInvocationCount.Should().Be(1);
        items.Should().ContainSingle();
        items[0].Project.Should().Be("外观");
        items[0].Specification.Should().Be("无划伤");
    }

    [Fact]
    public async Task DocumentTableAccessService_ShouldReturnSameExtraction_WhenSnapshotCacheEnabledOrDisabled()
    {
        var enabledHarness = await SnapshotTestHarness.CreateAsync(enabled: true);
        var disabledHarness = await SnapshotTestHarness.CreateAsync(enabled: false);
        var mapping = new ColumnMapping
        {
            HeaderRowIndex = 0,
            HeaderRowCount = 1,
            DataStartRowIndex = 1
        };

        var enabledService = enabledHarness.CreateTableAccessService();
        var disabledService = disabledHarness.CreateTableAccessService();

        var enabledTables = await enabledService.GetTablesAsync(enabledHarness.WordFile);
        var disabledTables = await disabledService.GetTablesAsync(disabledHarness.WordFile);
        enabledTables.Should().BeEquivalentTo(disabledTables, options => options.WithStrictOrdering());

        var enabledData = await enabledService.ExtractTableDataAsync(enabledHarness.WordFile, 0, mapping);
        var disabledData = await disabledService.ExtractTableDataAsync(disabledHarness.WordFile, 0, mapping);
        enabledData.Headers.Should().Equal(disabledData.Headers);
        enabledData.Rows.Select(row => row.GetValue(0)).Should()
            .Equal(disabledData.Rows.Select(row => row.GetValue(0)));

        var enabledItems = await enabledService.ExtractMatchSourceItemsAsync(
            enabledHarness.WordFile,
            0,
            0,
            1);
        var disabledItems = await disabledService.ExtractMatchSourceItemsAsync(
            disabledHarness.WordFile,
            0,
            0,
            1);
        enabledItems.Should().BeEquivalentTo(disabledItems, options => options.WithStrictOrdering());

        enabledHarness.SnapshotService.ParseInvocationCount.Should().Be(1);
        disabledHarness.SnapshotService.ParseInvocationCount.Should().Be(3);
    }

    private sealed class SnapshotTestHarness
    {
        private SnapshotTestHarness(
            UploadedDocumentSnapshotService snapshotService,
            WordFile wordFile)
        {
            SnapshotService = snapshotService;
            WordFile = wordFile;
        }

        public UploadedDocumentSnapshotService SnapshotService { get; }

        public WordFile WordFile { get; }

        public static async Task<SnapshotTestHarness> CreateAsync(bool enabled = true)
        {
            var root = Path.Combine(Path.GetTempPath(), "uploaded-snapshot-cross-scope", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var relativePath = "uploads/excel-files/2026-08-11/cross-scope.xlsx";
            var absolutePath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            var content = CreateWorkbookBytes();
            await File.WriteAllBytesAsync(absolutePath, content);
            var wordFile = new WordFile
            {
                Id = Random.Shared.Next(1000, 9999),
                FileName = "cross-scope.xlsx",
                FileType = UploadedFileType.ExcelXlsx,
                FilePath = relativePath,
                FileHash = FileStorageService.ComputeSha256(content)
            };
            var snapshotService = UploadedDocumentSnapshotServiceTests.CreateService(root, enabled);
            return new SnapshotTestHarness(snapshotService, wordFile);
        }

        public DocumentTableAccessService CreateTableAccessService() =>
            new(
                new DocumentServiceFactory(),
                SnapshotService,
                new ResourceBudgetGovernor(Microsoft.Extensions.Options.Options.Create(new ResourceBudgetOptions
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
                NullLogger<DocumentTableAccessService>.Instance);

        private static byte[] CreateWorkbookBytes()
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.AddWorksheet("测试表");
            sheet.Cell(1, 1).Value = "项目";
            sheet.Cell(1, 2).Value = "规格";
            sheet.Cell(2, 1).Value = "外观";
            sheet.Cell(2, 2).Value = "无划伤";
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}
