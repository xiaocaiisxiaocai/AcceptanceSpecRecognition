using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class ImportSpecificationOnlyBackfillTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ImportSpecificationOnlyBackfillTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ImportExcel_WhenSpecificationOnlyConfirmed_ShouldBackfillProjectFromSpecification()
    {
        var scope = await SeedImportScopeAsync();
        var fileId = await UploadExcelAsync(CreateSpecificationOnlyExcelBytes("规格内容", "验收标准", "备注", "SPEC-ONLY-EXCEL", "A1", "R1"));

        var response = await ImportExcelAsync(new
        {
            fileId,
            sheetIndex = 0,
            customerId = scope.CustomerId,
            processId = scope.ProcessId,
            headerRowStart = 1,
            headerRowCount = 1,
            dataStartRow = 2,
            specificationColumn = 1,
            acceptanceColumn = 2,
            remarkColumn = 3,
            isSpecificationOnly = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        json.Data.GetProperty("successCount").GetInt32().Should().Be(1);

        var spec = await FindSingleSpecAsync(scope.CustomerId, scope.ProcessId);
        spec.Project.Should().Be("SPEC-ONLY-EXCEL");
        spec.Specification.Should().Be("SPEC-ONLY-EXCEL");
        spec.Acceptance.Should().Be("A1");
        spec.Remark.Should().Be("R1");
    }

    [Fact]
    public async Task ImportExcel_WhenSameExecutionRequestIsConcurrent_ShouldCommitOnceAndReturnSameResult()
    {
        var scope = await SeedImportScopeAsync();
        var fileId = await UploadExcelAsync(CreateSpecificationOnlyExcelBytes(
            "规格内容", "验收标准", "备注", "IDEMPOTENT-SPEC", "A1", "R1"));
        var requestId = Guid.NewGuid().ToString("N");
        var payload = new
        {
            executionRequestId = requestId,
            fileId,
            sheetIndex = 0,
            customerId = scope.CustomerId,
            processId = scope.ProcessId,
            headerRowStart = 1,
            headerRowCount = 1,
            dataStartRow = 2,
            specificationColumn = 1,
            acceptanceColumn = 2,
            remarkColumn = 3,
            isSpecificationOnly = true,
            cleanupSourceFile = false
        };

        var first = ImportExcelAsync(payload);
        var second = ImportExcelAsync(payload);
        await Task.WhenAll(first, second);
        first.Result.StatusCode.Should().Be(HttpStatusCode.OK);
        second.Result.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.Result.ReadAsAsync<ApiResponse<JsonElement>>()).Data
            .GetProperty("successCount").GetInt32().Should().Be(1);
        (await second.Result.ReadAsAsync<ApiResponse<JsonElement>>()).Data
            .GetProperty("successCount").GetInt32().Should().Be(1);

        (await CountSpecsAsync(scope.CustomerId, scope.ProcessId)).Should().Be(1);
        using var verificationScope = _factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.DocumentImportExecutions.CountAsync(item => item.SourceFileId == fileId))
            .Should().Be(1);
    }

    [Fact]
    public async Task ImportWord_WhenSpecificationOnlyConfirmed_ShouldBackfillProjectFromSpecification()
    {
        var scope = await SeedImportScopeAsync();
        var fileId = await UploadWordAsync(CreateSpecificationOnlyDocxBytes(
            "规格内容",
            "验收标准",
            "备注",
            "SPEC-ONLY-WORD",
            "A1",
            "R1"));

        var response = await ImportWordAsync(new
        {
            fileId,
            tableIndex = 0,
            customerId = scope.CustomerId,
            processId = scope.ProcessId,
            isSpecificationOnly = true,
            mapping = new
            {
                specificationColumn = 0,
                acceptanceColumn = 1,
                remarkColumn = 2,
                headerRowIndex = 0,
                dataStartRowIndex = 1
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        json.Data.GetProperty("successCount").GetInt32().Should().Be(1);

        var spec = await FindSingleSpecAsync(scope.CustomerId, scope.ProcessId);
        spec.Project.Should().Be("SPEC-ONLY-WORD");
        spec.Specification.Should().Be("SPEC-ONLY-WORD");
        spec.Acceptance.Should().Be("A1");
        spec.Remark.Should().Be("R1");
    }

    [Fact]
    public async Task ImportWord_WhenFirstParsedDataRowExcluded_ShouldImportOnlySecondDataRow()
    {
        var scope = await SeedImportScopeAsync();
        var fileId = await UploadWordAsync(CreateSpecificationOnlyDocxBytes(
            "规格内容", "验收标准", "备注",
            "WORD-EXCLUDED", "A-EXCLUDED", "R-EXCLUDED",
            "WORD-KEPT", "A-KEPT", "R-KEPT"));

        var response = await ImportWordAsync(new
        {
            fileId,
            tableIndex = 0,
            customerId = scope.CustomerId,
            processId = scope.ProcessId,
            isSpecificationOnly = true,
            excludedRowIndexes = new[] { 0 },
            mapping = new
            {
                specificationColumn = 0,
                acceptanceColumn = 1,
                remarkColumn = 2,
                headerRowIndex = 0,
                dataStartRowIndex = 1
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Data.GetProperty("successCount").GetInt32().Should().Be(1);

        var spec = await FindSingleSpecAsync(scope.CustomerId, scope.ProcessId);
        spec.Project.Should().Be("WORD-KEPT");
        spec.Specification.Should().Be("WORD-KEPT");
    }

    [Fact]
    public async Task ImportExcel_WhenProjectColumnMissingWithoutSpecificationOnlyConfirmation_ShouldReject()
    {
        var scope = await SeedImportScopeAsync();
        var fileId = await UploadExcelAsync(CreateSpecificationOnlyExcelBytes("项目", "规格内容", "备注", "P-MISSED", "SPEC-MISSED", "R1"));

        var response = await ImportExcelAsync(new
        {
            fileId,
            sheetIndex = 0,
            customerId = scope.CustomerId,
            processId = scope.ProcessId,
            headerRowStart = 1,
            headerRowCount = 1,
            dataStartRow = 2,
            specificationColumn = 2,
            remarkColumn = 3
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Message.Should().Contain("仅规格");

        var count = await CountSpecsAsync(scope.CustomerId, scope.ProcessId);
        count.Should().Be(0);
    }

    [Fact]
    public async Task ImportExcel_WhenSpecificationOnlyBackfillMatchesExistingProjectAndSpecification_ShouldSkipDuplicate()
    {
        var scope = await SeedImportScopeAsync();
        await SeedExistingSpecAsync(scope.CustomerId, scope.ProcessId, "SPEC-DUP", "SPEC-DUP", "旧验收", "旧备注");
        var fileId = await UploadExcelAsync(CreateSpecificationOnlyExcelBytes("规格内容", "验收标准", "备注", "SPEC-DUP", "旧验收", "旧备注"));

        var response = await ImportExcelAsync(new
        {
            fileId,
            sheetIndex = 0,
            customerId = scope.CustomerId,
            processId = scope.ProcessId,
            headerRowStart = 1,
            headerRowCount = 1,
            dataStartRow = 2,
            specificationColumn = 1,
            acceptanceColumn = 2,
            remarkColumn = 3,
            isSpecificationOnly = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        json.Data.GetProperty("successCount").GetInt32().Should().Be(0);
        json.Data.GetProperty("skippedCount").GetInt32().Should().Be(1);

        var count = await CountSpecsAsync(scope.CustomerId, scope.ProcessId);
        count.Should().Be(1);
    }

    private async Task<(int CustomerId, int ProcessId)> SeedImportScopeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var customer = new Customer
        {
            Name = $"仅规格导入客户-{suffix}",
            CreatedAt = DateTime.UtcNow
        };
        var process = new AcceptanceSpecSystem.Data.Entities.Process
        {
            Name = $"仅规格导入制程-{suffix}",
            CreatedAt = DateTime.UtcNow
        };

        db.Customers.Add(customer);
        db.Processes.Add(process);
        await db.SaveChangesAsync();
        return (customer.Id, process.Id);
    }

    private async Task<AcceptanceSpec> FindSingleSpecAsync(int customerId, int processId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AcceptanceSpecs.SingleAsync(spec =>
            spec.CustomerId == customerId &&
            spec.ProcessId == processId);
    }

    private async Task<int> CountSpecsAsync(int customerId, int processId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AcceptanceSpecs.CountAsync(spec =>
            spec.CustomerId == customerId &&
            spec.ProcessId == processId);
    }

    private async Task SeedExistingSpecAsync(
        int customerId,
        int processId,
        string project,
        string specification,
        string acceptance,
        string remark)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var wordFile = new WordFile
        {
            FileName = $"existing-spec-only-{Guid.NewGuid():N}.xlsx",
            FileContent = Array.Empty<byte>(),
            FileHash = Guid.NewGuid().ToString("N"),
            UploadedAt = DateTime.UtcNow,
            FileType = UploadedFileType.ExcelXlsx
        };

        db.WordFiles.Add(wordFile);
        await db.SaveChangesAsync();

        db.AcceptanceSpecs.Add(new AcceptanceSpec
        {
            CustomerId = customerId,
            ProcessId = processId,
            Project = project,
            Specification = specification,
            Acceptance = acceptance,
            Remark = remark,
            WordFileId = wordFile.Id,
            OwnerOrgUnitId = 1,
            CreatedByUserId = 1,
            ImportedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task<int> UploadExcelAsync(byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", $"spec-only-{Guid.NewGuid():N}.xlsx");

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private async Task<int> UploadWordAsync(byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", $"spec-only-{Guid.NewGuid():N}.docx");

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private Task<HttpResponseMessage> ImportExcelAsync(object payload)
    {
        return _client.PostAsync(
            "/api/documents/excel/import",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
    }

    private Task<HttpResponseMessage> ImportWordAsync(object payload)
    {
        return _client.PostAsync(
            "/api/documents/import",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
    }

    private static byte[] CreateSpecificationOnlyExcelBytes(
        string header1,
        string header2,
        string header3,
        string value1,
        string value2,
        string value3)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Sheet1");
        worksheet.Cell(1, 1).Value = header1;
        worksheet.Cell(1, 2).Value = header2;
        worksheet.Cell(1, 3).Value = header3;
        worksheet.Cell(2, 1).Value = value1;
        worksheet.Cell(2, 2).Value = value2;
        worksheet.Cell(2, 3).Value = value3;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateSpecificationOnlyDocxBytes(params string[] cells)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var table = new Table();

            for (var index = 0; index < cells.Length; index += 3)
            {
                var row = new TableRow();
                for (var offset = 0; offset < 3; offset++)
                {
                    row.AppendChild(new TableCell(
                        new Paragraph(
                            new Run(
                                new Text(cells[index + offset] ?? string.Empty)))));
                }

                table.AppendChild(row);
            }

            mainPart.Document.Body!.AppendChild(table);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }
}
