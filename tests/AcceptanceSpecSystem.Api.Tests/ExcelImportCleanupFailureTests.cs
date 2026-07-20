using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class ExcelImportCleanupFailureTests
    : IClassFixture<RowFailureEmbeddingApiWebApplicationFactory>
{
    private readonly RowFailureEmbeddingApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ExcelImportCleanupFailureTests(RowFailureEmbeddingApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Import_WithFailedRowsAndCleanupRequested_ShouldKeepPhysicalSourceForRetry()
    {
        int customerId;
        int processId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer
            {
                Name = $"失败清理客户-{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow
            };
            var process = new Process
            {
                Name = $"失败清理制程-{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow
            };
            db.Customers.Add(customer);
            db.Processes.Add(process);
            await db.SaveChangesAsync();

            var existingFile = new WordFile
            {
                FileName = $"existing-{Guid.NewGuid():N}.xlsx",
                FileContent = [],
                FileHash = Guid.NewGuid().ToString("N"),
                UploadedAt = DateTime.UtcNow,
                FileType = UploadedFileType.ExcelXlsx
            };
            db.WordFiles.Add(existingFile);
            await db.SaveChangesAsync();
            db.AcceptanceSpecs.Add(new AcceptanceSpec
            {
                CustomerId = customer.Id,
                ProcessId = process.Id,
                Project = "已有项目",
                Specification = "已有规格",
                WordFileId = existingFile.Id,
                OwnerOrgUnitId = 1,
                CreatedByUserId = 1,
                ImportedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            customerId = customer.Id;
            processId = process.Id;
        }

        var fileId = await UploadExcelAsync(CreateExcelBytes());
        string sourcePathBeforeImport;
        string absoluteSourcePath;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            sourcePathBeforeImport = (await db.WordFiles.SingleAsync(item => item.Id == fileId)).FilePath!;
            absoluteSourcePath = scope.ServiceProvider
                .GetRequiredService<IFileStorageService>()
                .GetAbsolutePath(sourcePathBeforeImport);
        }
        sourcePathBeforeImport.Should().NotBeNullOrWhiteSpace();
        File.Exists(absoluteSourcePath).Should().BeTrue();

        var importResponse = await _client.PostAsync(
            "/api/documents/excel/import",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    fileId,
                    sheetIndex = 0,
                    customerId,
                    processId,
                    headerRowStart = 1,
                    headerRowCount = 1,
                    dataStartRow = 2,
                    projectColumn = 1,
                    specificationColumn = 2,
                    acceptanceColumn = 3,
                    remarkColumn = 4,
                    cleanupSourceFile = true,
                    duplicateCheckOptions = new
                    {
                        enableSemanticDuplicateCheck = true,
                        semanticMinScore = 0,
                        enableLlmDuplicateReview = false
                    }
                }),
                Encoding.UTF8,
                "application/json"));

        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var importJson = await importResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        importJson.Code.Should().Be(0);
        importJson.Data.GetProperty("failedCount").GetInt32().Should().Be(1);

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sourceFile = await verificationDb.WordFiles.SingleAsync(item => item.Id == fileId);
        sourceFile.FilePath.Should().Be(sourcePathBeforeImport);
        File.Exists(absoluteSourcePath).Should().BeTrue(
            "失败区域必须保留物理文件，才能修正配置后安全重试后续区域");
    }

    private async Task<int> UploadExcelAsync(byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "cleanup-failed-source.xlsx");

        using var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Sheet1");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格内容";
        worksheet.Cell(1, 3).Value = "验收标准";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "触发行失败";
        worksheet.Cell(2, 2).Value = "触发行失败规格";
        worksheet.Cell(2, 3).Value = "验收";
        worksheet.Cell(2, 4).Value = "备注";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public sealed class RowFailureEmbeddingApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmbeddingService>();
            services.AddScoped<IEmbeddingService, RowFailureEmbeddingService>();
            services.RemoveAll<IImportEmbeddingCache>();
            services.AddScoped<IImportEmbeddingCache, FixedImportEmbeddingCache>();
        });
    }

    private sealed class RowFailureEmbeddingService : IEmbeddingService
    {
        public bool IsAvailable => true;

        public Task<float[]> GenerateEmbeddingAsync(
            string text,
            int? serviceId = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("模拟单行语义检测失败");

        public Task<List<float[]>> GenerateEmbeddingsAsync(
            IEnumerable<string> texts,
            int? serviceId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(texts.Select(_ => new[] { 1f }).ToList());

        public double ComputeSimilarity(float[] embedding1, float[] embedding2) => 1;
    }

    private sealed class FixedImportEmbeddingCache : IImportEmbeddingCache
    {
        public Task<IReadOnlyDictionary<int, float[]>> GetImportDuplicateEmbeddingsAsync(
            IReadOnlyCollection<AcceptanceSpec> specs,
            int? embeddingServiceId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<int, float[]>>(
                specs.ToDictionary(item => item.Id, _ => new[] { 1f }));

        public Task RemoveSpecCachesAsync(int specId) => Task.CompletedTask;
    }
}
