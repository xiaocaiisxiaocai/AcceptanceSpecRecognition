using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class ImportDuplicateDetectionTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ImportDuplicateDetectionTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ImportExcel_WhenDatabaseContainsExactDuplicate_ShouldRequireConfirmationAndOverwriteExisting()
    {
        var seeded = await SeedImportScopeAsync();
        await SeedExistingSpecAsync(
            seeded.CustomerId,
            seeded.ProcessId,
            "P-EXACT",
            "S-EXACT",
            "A-EXACT",
            "R-EXACT");

        var fileId = await UploadExcelAsync(CreateExcelBytes(new[]
        {
            ("P-EXACT", "S-EXACT", "A-EXACT", "R-EXACT")
        }), "exact-duplicate.xlsx");

        var firstResponse = await ImportExcelAsync(new
        {
            fileId,
            sheetIndex = 0,
            customerId = seeded.CustomerId,
            processId = seeded.ProcessId,
            headerRowStart = 1,
            headerRowCount = 1,
            dataStartRow = 2,
            projectColumn = 1,
            specificationColumn = 2,
            acceptanceColumn = 3,
            remarkColumn = 4
        });

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstJson = await firstResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        firstJson.Code.Should().Be(0);
        firstJson.Data.GetProperty("requiresConfirmation").GetBoolean().Should().BeTrue();
        firstJson.Data.GetProperty("pendingCount").GetInt32().Should().Be(1);
        var pending = firstJson.Data.GetProperty("pendingDifferences")[0];
        pending.GetProperty("matchType").GetString().Should().Be("exact");
        var pendingKey = pending.GetProperty("key").GetString();
        pendingKey.Should().NotBeNullOrWhiteSpace();

        var confirmResponse = await ImportExcelAsync(new
        {
            fileId,
            sheetIndex = 0,
            customerId = seeded.CustomerId,
            processId = seeded.ProcessId,
            headerRowStart = 1,
            headerRowCount = 1,
            dataStartRow = 2,
            projectColumn = 1,
            specificationColumn = 2,
            acceptanceColumn = 3,
            remarkColumn = 4,
            confirmedDifferenceKeys = new[] { pendingKey }
        });

        confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmJson = await confirmResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        confirmJson.Code.Should().Be(0);
        confirmJson.Data.GetProperty("successCount").GetInt32().Should().Be(1);
        confirmJson.Data.GetProperty("skippedCount").GetInt32().Should().Be(0);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var specs = await dbContext.AcceptanceSpecs
            .Where(spec => spec.CustomerId == seeded.CustomerId && spec.ProcessId == seeded.ProcessId)
            .ToListAsync();

        specs.Should().HaveCount(1);
        specs[0].Project.Should().Be("P-EXACT");
        specs[0].Specification.Should().Be("S-EXACT");
        specs[0].Acceptance.Should().Be("A-EXACT");
        specs[0].Remark.Should().Be("R-EXACT");
        specs[0].WordFileId.Should().Be(fileId);
    }

    [Fact]
    public async Task ImportExcel_WhenSemanticDuplicateEnabled_ShouldReturnSemanticPendingDifference()
    {
        var seeded = await SeedImportScopeAsync();
        await SeedExistingSpecAsync(
            seeded.CustomerId,
            seeded.ProcessId,
            "平台吸附精度",
            "平台平面度需控制在0.05mm以内",
            "旧验收",
            "旧备注");

        var fileId = await UploadExcelAsync(CreateExcelBytes(new[]
        {
            ("平台精度", "平面度控制在0.05mm以内", "新验收", "新备注")
        }), "semantic-duplicate.xlsx");

        var response = await ImportExcelAsync(new
        {
            fileId,
            sheetIndex = 0,
            customerId = seeded.CustomerId,
            processId = seeded.ProcessId,
            headerRowStart = 1,
            headerRowCount = 1,
            dataStartRow = 2,
            projectColumn = 1,
            specificationColumn = 2,
            acceptanceColumn = 3,
            remarkColumn = 4,
            duplicateCheckOptions = new
            {
                enableSemanticDuplicateCheck = true,
                semanticTopK = 3,
                semanticMinScore = 0.1,
                enableLlmDuplicateReview = true,
                llmPassScore = 0.3,
                highConfidenceThreshold = 0.95
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        json.Data.GetProperty("requiresConfirmation").GetBoolean().Should().BeTrue();
        json.Data.GetProperty("pendingCount").GetInt32().Should().Be(1);
        var pending = json.Data.GetProperty("pendingDifferences")[0];
        pending.GetProperty("matchType").GetString().Should().Be("semantic");
        pending.GetProperty("embeddingScore").GetDouble().Should().BeGreaterThan(0.1);
        pending.GetProperty("llmScore").GetDouble().Should().BeGreaterThan(0.39);
    }

    [Fact]
    public async Task ImportExcel_WhenUserChoosesPartialOverwrite_ShouldKeepProjectAndSpecification()
    {
        var seeded = await SeedImportScopeAsync();
        await SeedExistingSpecAsync(
            seeded.CustomerId,
            seeded.ProcessId,
            "平台吸附精度",
            "平台平面度需控制在0.05mm以内",
            "旧验收",
            "旧备注");

        var fileId = await UploadExcelAsync(CreateExcelBytes(new[]
        {
            ("平台精度", "平面度控制在0.05mm以内", "新验收", "新备注")
        }), "semantic-partial-overwrite.xlsx");

        var firstResponse = await ImportExcelAsync(new
        {
            fileId,
            sheetIndex = 0,
            customerId = seeded.CustomerId,
            processId = seeded.ProcessId,
            headerRowStart = 1,
            headerRowCount = 1,
            dataStartRow = 2,
            projectColumn = 1,
            specificationColumn = 2,
            acceptanceColumn = 3,
            remarkColumn = 4,
            duplicateCheckOptions = new
            {
                enableSemanticDuplicateCheck = true,
                semanticTopK = 3,
                semanticMinScore = 0.1,
                enableLlmDuplicateReview = true,
                llmPassScore = 0.3,
                highConfidenceThreshold = 0.95
            }
        });

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstJson = await firstResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        firstJson.Code.Should().Be(0);
        firstJson.Data.GetProperty("requiresConfirmation").GetBoolean().Should().BeTrue();
        var pending = firstJson.Data.GetProperty("pendingDifferences")[0];
        pending.GetProperty("matchType").GetString().Should().Be("semantic");
        var pendingKey = pending.GetProperty("key").GetString();
        pendingKey.Should().NotBeNullOrWhiteSpace();

        var confirmResponse = await ImportExcelAsync(new
        {
            fileId,
            sheetIndex = 0,
            customerId = seeded.CustomerId,
            processId = seeded.ProcessId,
            headerRowStart = 1,
            headerRowCount = 1,
            dataStartRow = 2,
            projectColumn = 1,
            specificationColumn = 2,
            acceptanceColumn = 3,
            remarkColumn = 4,
            partiallyConfirmedDifferenceKeys = new[] { pendingKey },
            duplicateCheckOptions = new
            {
                enableSemanticDuplicateCheck = true,
                semanticTopK = 3,
                semanticMinScore = 0.1,
                enableLlmDuplicateReview = true,
                llmPassScore = 0.3,
                highConfidenceThreshold = 0.95
            }
        });

        confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmJson = await confirmResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        confirmJson.Code.Should().Be(0);
        confirmJson.Data.GetProperty("successCount").GetInt32().Should().Be(1);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var specs = await dbContext.AcceptanceSpecs
            .Where(spec => spec.CustomerId == seeded.CustomerId && spec.ProcessId == seeded.ProcessId)
            .ToListAsync();

        specs.Should().HaveCount(1);
        specs[0].Project.Should().Be("平台吸附精度");
        specs[0].Specification.Should().Be("平台平面度需控制在0.05mm以内");
        specs[0].Acceptance.Should().Be("新验收");
        specs[0].Remark.Should().Be("新备注");
        specs[0].WordFileId.Should().Be(fileId);
    }

    private async Task<(int CustomerId, int ProcessId)> SeedImportScopeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var customer = new Customer
        {
            Name = $"导入客户-{suffix}",
            CreatedAt = DateTime.UtcNow
        };
        var process = new Process
        {
            Name = $"导入制程-{suffix}",
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Customers.Add(customer);
        dbContext.Processes.Add(process);
        await dbContext.SaveChangesAsync();

        return (customer.Id, process.Id);
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
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var wordFile = new WordFile
        {
            FileName = $"existing-{suffix}.xlsx",
            FileContent = Array.Empty<byte>(),
            FileHash = Guid.NewGuid().ToString("N"),
            UploadedAt = DateTime.UtcNow,
            FileType = UploadedFileType.ExcelXlsx
        };

        dbContext.WordFiles.Add(wordFile);
        await dbContext.SaveChangesAsync();

        dbContext.AcceptanceSpecs.Add(new AcceptanceSpec
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
        await dbContext.SaveChangesAsync();
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        using var response = await _client.PostAsync("/api/documents/upload", content);
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

    private static byte[] CreateExcelBytes(IEnumerable<(string Project, string Specification, string Acceptance, string Remark)> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Sheet1");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格内容";
        worksheet.Cell(1, 3).Value = "验收标准";
        worksheet.Cell(1, 4).Value = "备注";

        var currentRow = 2;
        foreach (var row in rows)
        {
            worksheet.Cell(currentRow, 1).Value = row.Project;
            worksheet.Cell(currentRow, 2).Value = row.Specification;
            worksheet.Cell(currentRow, 3).Value = row.Acceptance;
            worksheet.Cell(currentRow, 4).Value = row.Remark;
            currentRow++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
