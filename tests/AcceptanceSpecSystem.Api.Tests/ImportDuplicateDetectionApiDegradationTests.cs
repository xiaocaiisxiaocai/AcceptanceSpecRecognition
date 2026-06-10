using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Api.Tests;

public class ImportDuplicateDetectionApiAvailabilityTests : IClassFixture<FailingEmbeddingApiWebApplicationFactory>
{
    private readonly FailingEmbeddingApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ImportDuplicateDetectionApiAvailabilityTests(FailingEmbeddingApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ImportExcel_WhenAiModeEnabledAndEmbeddingUnavailable_ShouldReturnFriendlyBadRequest()
    {
        var seeded = await SeedImportScopeAsync();
        await SeedExistingSpecAsync(
            seeded.CustomerId,
            seeded.ProcessId,
            "旧项目",
            "旧规格");

        var fileId = await UploadExcelAsync(CreateExcelBytes(new[]
        {
            ("新项目", "新规格", "新验收", "新备注")
        }), "ai-enabled.xlsx");

        using var response = await _client.PostAsync(
            "/api/documents/excel/import",
            ApiClientJson.ToJsonContent(new
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
                    enableSemanticDuplicateCheck = true
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(400);
        json.Message.Should().Contain("AI 疑似重复识别不可用");
        json.Message.Should().Contain("关闭 AI 模式后重试");
    }

    [Fact]
    public async Task ImportExcel_WhenAiModeDisabled_ShouldIgnoreUnavailableAiServiceAndSucceed()
    {
        var seeded = await SeedImportScopeAsync();
        var fileId = await UploadExcelAsync(CreateExcelBytes(new[]
        {
            ("新项目", "新规格", "新验收", "新备注")
        }), "ai-disabled.xlsx");

        using var response = await _client.PostAsync(
            "/api/documents/excel/import",
            ApiClientJson.ToJsonContent(new
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
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        json.Data.GetProperty("successCount").GetInt32().Should().Be(1);
        json.Message.Should().Contain("导入完成");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var specs = await dbContext.AcceptanceSpecs
            .Where(spec => spec.CustomerId == seeded.CustomerId && spec.ProcessId == seeded.ProcessId)
            .ToListAsync();

        specs.Should().ContainSingle(spec => spec.Project == "新项目" && spec.Specification == "新规格");
    }

    [Fact]
    public async Task ImportExcel_WhenConfirmationReplayContainsDecision_ShouldNotRequireEmbeddingService()
    {
        var seeded = await SeedImportScopeAsync();
        await SeedExistingSpecAsync(
            seeded.CustomerId,
            seeded.ProcessId,
            "旧项目",
            "旧规格");

        var fileId = await UploadExcelAsync(CreateExcelBytes(new[]
        {
            ("旧项目", "旧规格", "新验收", "新备注")
        }), "confirm-replay.xlsx");

        using var firstResponse = await _client.PostAsync(
            "/api/documents/excel/import",
            ApiClientJson.ToJsonContent(new
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
            }));

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstJson = await firstResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        firstJson.Code.Should().Be(0);
        firstJson.Data.GetProperty("requiresConfirmation").GetBoolean().Should().BeTrue();
        var pendingKey = firstJson.Data.GetProperty("pendingDifferences")[0].GetProperty("key").GetString();
        pendingKey.Should().NotBeNullOrWhiteSpace();

        using var confirmResponse = await _client.PostAsync(
            "/api/documents/excel/import",
            ApiClientJson.ToJsonContent(new
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
                confirmedDifferenceKeys = new[] { pendingKey },
                duplicateCheckOptions = new
                {
                    enableSemanticDuplicateCheck = true
                }
            }));

        confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmJson = await confirmResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        confirmJson.Code.Should().Be(0);
        confirmJson.Data.GetProperty("successCount").GetInt32().Should().Be(1);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var spec = await dbContext.AcceptanceSpecs
            .SingleAsync(item => item.CustomerId == seeded.CustomerId && item.ProcessId == seeded.ProcessId);

        spec.Project.Should().Be("旧项目");
        spec.Specification.Should().Be("旧规格");
        spec.Acceptance.Should().Be("新验收");
        spec.Remark.Should().Be("新备注");
    }

    private async Task<(int CustomerId, int ProcessId)> SeedImportScopeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var customer = new Customer
        {
            Name = $"可用性客户-{suffix}",
            CreatedAt = DateTime.UtcNow
        };
        var process = new Process
        {
            Name = $"可用性制程-{suffix}",
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Customers.Add(customer);
        dbContext.Processes.Add(process);
        await dbContext.SaveChangesAsync();

        return (customer.Id, process.Id);
    }

    private async Task SeedExistingSpecAsync(int customerId, int processId, string project, string specification)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var wordFile = new WordFile
        {
            FileName = $"existing-{Guid.NewGuid():N}.xlsx",
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
            Acceptance = "旧验收",
            Remark = "旧备注",
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

public sealed class FailingEmbeddingApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IEmbeddingService));
            services.AddScoped<IEmbeddingService, AlwaysFailEmbeddingService>();
        });
    }

    private sealed class AlwaysFailEmbeddingService : IEmbeddingService
    {
        public bool IsAvailable => false;

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
            => throw new AiServiceUnavailableException("Embedding 服务不可用");

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
            => throw new AiServiceUnavailableException("Embedding 服务不可用");

        public double ComputeSimilarity(float[] embedding1, float[] embedding2) => 0;
    }
}
