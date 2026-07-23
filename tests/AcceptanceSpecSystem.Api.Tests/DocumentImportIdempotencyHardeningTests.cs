using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class DocumentImportIdempotencyHardeningTests
    : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public DocumentImportIdempotencyHardeningTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ImportReplay_WhenFileAccessWasRevoked_ShouldNotReturnStoredResult()
    {
        using var client = CreateCommonUserClient();
        var customerId = await CreateCustomerAsync();
        var fileId = await UploadExcelAsync(client, CreateExcelBytes("权限回放项目"));
        var requestId = $"auth-replay-{Guid.NewGuid():N}";
        var payload = CreateImportPayload(fileId, customerId, requestId, cleanupSourceFile: false);

        using (var firstResponse = await PostImportAsync(client, payload))
        {
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var firstJson = await firstResponse.ReadAsAsync<ApiResponse<JsonElement>>();
            firstJson.Code.Should().Be(0);
            firstJson.Data.GetProperty("successCount").GetInt32().Should().Be(1);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var file = await db.WordFiles.SingleAsync(item => item.Id == fileId);
            file.CreatedByUserId = 1;
            file.OwnerOrgUnitId = null;
            var importedSpecs = await db.AcceptanceSpecs
                .Where(item => item.WordFileId == fileId)
                .ToListAsync();
            foreach (var spec in importedSpecs)
            {
                spec.CreatedByUserId = 1;
                spec.OwnerOrgUnitId = null;
            }
            await db.SaveChangesAsync();
        }

        using var replayResponse = await PostImportAsync(client, payload);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var replayJson = await replayResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        replayJson.Code.Should().Be(400);
        replayJson.Message.Should().Contain("无权访问");
    }

    [Fact]
    public async Task ImportReplay_WhenCleanupIsPending_ShouldCompleteCleanupWithoutReimporting()
    {
        using var client = _factory.CreateClient();
        var customerId = await CreateCustomerAsync();
        var excelBytes = CreateExcelBytes("清理补偿项目");
        var fileId = await UploadExcelAsync(client, excelBytes);
        var requestId = $"cleanup-replay-{Guid.NewGuid():N}";
        var payload = CreateImportPayload(fileId, customerId, requestId, cleanupSourceFile: true);

        using (var firstResponse = await PostImportAsync(client, payload))
        {
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var firstJson = await firstResponse.ReadAsAsync<ApiResponse<JsonElement>>();
            firstJson.Code.Should().Be(0);
            firstJson.Data.GetProperty("successCount").GetInt32().Should().Be(1);
        }

        string pendingPhysicalPath;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            var file = await db.WordFiles.SingleAsync(item => item.Id == fileId);
            var execution = await db.DocumentImportExecutions.SingleAsync(item => item.SourceFileId == fileId);

            var pendingPath = await storage.SaveUploadedExcelAsync(file.FileName, file.FileContent);
            pendingPhysicalPath = storage.GetAbsolutePath(pendingPath);
            file.FilePath = pendingPath;
            file.FileContent = [];
            execution.CleanupRequested = true;
            execution.CleanupCompleted = false;
            await db.SaveChangesAsync();
        }
        File.Exists(pendingPhysicalPath).Should().BeTrue();

        using (var replayResponse = await PostImportAsync(client, payload))
        {
            replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var replayJson = await replayResponse.ReadAsAsync<ApiResponse<JsonElement>>();
            replayJson.Code.Should().Be(0);
            replayJson.Data.GetProperty("successCount").GetInt32().Should().Be(1);
        }

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sourceFile = await verificationDb.WordFiles.SingleAsync(item => item.Id == fileId);
        var storedExecution = await verificationDb.DocumentImportExecutions.SingleAsync(item => item.SourceFileId == fileId);
        sourceFile.FilePath.Should().BeNull();
        sourceFile.FileContent.Should().NotBeEmpty("清理后仍需保持历史文件可读取");
        storedExecution.CleanupRequested.Should().BeTrue();
        storedExecution.CleanupCompleted.Should().BeTrue();
        (await verificationDb.AcceptanceSpecs.CountAsync(item => item.WordFileId == fileId)).Should().Be(1);
        File.Exists(pendingPhysicalPath).Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentImports_WithDifferentRequestIdsInSameScope_ShouldNotCreateDuplicateSpecs()
    {
        using var client = _factory.CreateClient();
        var customerId = await CreateCustomerAsync();
        var fileId = await UploadExcelAsync(client, CreateExcelBytes("并发作用域项目"));
        var firstPayload = CreateImportPayload(
            fileId,
            customerId,
            $"scope-a-{Guid.NewGuid():N}",
            cleanupSourceFile: false);
        var secondPayload = CreateImportPayload(
            fileId,
            customerId,
            $"scope-b-{Guid.NewGuid():N}",
            cleanupSourceFile: false);

        var responses = await Task.WhenAll(
            PostImportAsync(client, firstPayload),
            PostImportAsync(client, secondPayload));
        try
        {
            responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AcceptanceSpecs.CountAsync(item =>
            item.CustomerId == customerId && item.Project == "并发作用域项目"))
            .Should().Be(1);
        (await db.DocumentImportExecutions.CountAsync(item => item.SourceFileId == fileId))
            .Should().Be(2);
    }

    [Fact]
    public async Task PartialConfirmation_WithPendingRows_ShouldPersistAndReplaySnapshot()
    {
        using var client = _factory.CreateClient();
        var customerId = await CreateCustomerAsync();
        await SeedExistingSpecAsync(customerId, "部分确认项目1", "部分确认规格1", "旧验收1");
        await SeedExistingSpecAsync(customerId, "部分确认项目2", "部分确认规格2", "旧验收2");
        var fileId = await UploadExcelAsync(client, CreateExcelBytes(
            ("部分确认项目1", "部分确认规格1", "新验收1", "新备注1"),
            ("部分确认项目2", "部分确认规格2", "新验收2", "新备注2")));

        var basePayload = new
        {
            fileId,
            sheetIndex = 0,
            customerId,
            headerRowStart = 1,
            headerRowCount = 1,
            dataStartRow = 2,
            projectColumn = 1,
            specificationColumn = 2,
            acceptanceColumn = 3,
            remarkColumn = 4,
            cleanupSourceFile = false,
            duplicateCheckOptions = new
            {
                enableSemanticDuplicateCheck = false,
                enableLlmDuplicateReview = false
            }
        };
        using var previewResponse = await PostImportAsync(client, basePayload);
        var previewJson = await previewResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var firstDecisionToken = previewJson.Data
            .GetProperty("pendingDifferences")[0]
            .GetProperty("key")
            .GetString()!;
        var requestId = $"partial-{Guid.NewGuid():N}";
        var confirmedPayload = new
        {
            executionRequestId = requestId,
            basePayload.fileId,
            basePayload.sheetIndex,
            basePayload.customerId,
            basePayload.headerRowStart,
            basePayload.headerRowCount,
            basePayload.dataStartRow,
            basePayload.projectColumn,
            basePayload.specificationColumn,
            basePayload.acceptanceColumn,
            basePayload.remarkColumn,
            basePayload.cleanupSourceFile,
            basePayload.duplicateCheckOptions,
            confirmedDifferenceKeys = new[] { firstDecisionToken }
        };

        using (var firstConfirmedResponse = await PostImportAsync(client, confirmedPayload))
        {
            firstConfirmedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var firstJson = await firstConfirmedResponse.ReadAsAsync<ApiResponse<JsonElement>>();
            firstJson.Data.GetProperty("pendingCount").GetInt32().Should().Be(1);
        }
        using (var replayResponse = await PostImportAsync(client, confirmedPayload))
        {
            replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var replayJson = await replayResponse.ReadAsAsync<ApiResponse<JsonElement>>();
            replayJson.Data.GetProperty("pendingCount").GetInt32().Should().Be(1);
        }

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var execution = await verificationDb.DocumentImportExecutions
            .SingleAsync(item => item.SourceFileId == fileId);
        var storedResult = JsonSerializer.Deserialize<JsonElement>(execution.ResultJson);
        storedResult.GetProperty("pendingCount").GetInt32().Should().Be(1);
        storedResult.GetProperty("skippedRows").GetArrayLength().Should().Be(0);
        (await verificationDb.AcceptanceSpecs.SingleAsync(item =>
            item.CustomerId == customerId && item.Project == "部分确认项目1"))
            .Acceptance.Should().Be("新验收1");
    }

    [Fact]
    public async Task EmptyExcel_WithIdempotencyAndCleanup_ShouldPersistSnapshotAndCleanupSource()
    {
        using var client = _factory.CreateClient();
        var customerId = await CreateCustomerAsync();
        var fileId = await UploadExcelAsync(client, CreateEmptyExcelBytes());
        var payload = CreateImportPayload(
            fileId,
            customerId,
            $"empty-{Guid.NewGuid():N}",
            cleanupSourceFile: true);

        using var response = await PostImportAsync(client, payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var execution = await db.DocumentImportExecutions.SingleAsync(item => item.SourceFileId == fileId);
        var file = await db.WordFiles.SingleAsync(item => item.Id == fileId);
        execution.CleanupRequested.Should().BeTrue();
        execution.CleanupCompleted.Should().BeTrue();
        execution.ExpiresAt.Should().BeAfter(execution.CreatedAt);
        file.FilePath.Should().BeNull();
        file.FileContent.Should().NotBeEmpty("空表导入清理后仍需保持历史文件可读取");
    }

    [Fact]
    public async Task Import_WhenUnrelatedDatabaseWriteFails_ShouldReturnServerErrorInsteadOfIdempotencyConflict()
    {
        using var client = _factory.CreateClient();
        var customerId = await CreateCustomerAsync();
        var fileId = await UploadExcelAsync(client, CreateExcelBytes("非幂等数据库故障"));
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER \"fail_document_import_test\" BEFORE INSERT ON \"AcceptanceSpecs\" BEGIN SELECT RAISE(ABORT, 'forced unrelated import failure'); END;");
        }

        try
        {
            var payload = CreateImportPayload(
                fileId,
                customerId,
                $"db-failure-{Guid.NewGuid():N}",
                cleanupSourceFile: false);
            using var response = await PostImportAsync(client, payload);
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
            json.Code.Should().Be(500);
        }
        finally
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS \"fail_document_import_test\";");
        }
    }

    [Fact]
    public async Task IdempotentImport_ShouldDeleteExpiredExecutionSnapshots()
    {
        using var client = _factory.CreateClient();
        var customerId = await CreateCustomerAsync();
        var fileId = await UploadExcelAsync(client, CreateExcelBytes("快照清理项目"));
        int expiredExecutionId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var expired = new DocumentImportExecution
            {
                RequestKey = $"expired_{Guid.NewGuid():N}",
                RequestFingerprint = new string('A', 64),
                SourceFileId = fileId,
                CreatedByUserId = 1,
                CompanyId = 1,
                ResultJson = "{}",
                Message = "expired",
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                ExpiresAt = DateTime.UtcNow.AddDays(-1)
            };
            db.DocumentImportExecutions.Add(expired);
            await db.SaveChangesAsync();
            expiredExecutionId = expired.Id;
        }

        var payload = CreateImportPayload(
            fileId,
            customerId,
            $"ttl-{Guid.NewGuid():N}",
            cleanupSourceFile: false);
        using var response = await PostImportAsync(client, payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verificationDb.DocumentImportExecutions.AnyAsync(item => item.Id == expiredExecutionId))
            .Should().BeFalse();
    }

    [Fact]
    public async Task SameClientRequestId_AcrossDifferentUsers_ShouldCreateIndependentScopedExecutions()
    {
        using var adminClient = _factory.CreateClient();
        using var commonClient = CreateCommonUserClient();
        var customerId = await CreateCustomerAsync();
        var adminFileId = await UploadExcelAsync(adminClient, CreateExcelBytes("管理员幂等项目"));
        var commonFileId = await UploadExcelAsync(commonClient, CreateExcelBytes("普通用户幂等项目"));
        var sharedClientRequestId = $"shared-{Guid.NewGuid():N}";

        using var adminResponse = await PostImportAsync(
            adminClient,
            CreateImportPayload(adminFileId, customerId, sharedClientRequestId, cleanupSourceFile: false));
        using var commonResponse = await PostImportAsync(
            commonClient,
            CreateImportPayload(commonFileId, customerId, sharedClientRequestId, cleanupSourceFile: false));
        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        commonResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var executions = await db.DocumentImportExecutions
            .Where(item => item.SourceFileId == adminFileId || item.SourceFileId == commonFileId)
            .ToListAsync();
        executions.Should().HaveCount(2);
        executions.Select(item => item.RequestKey).Should().OnlyHaveUniqueItems();
        executions.Select(item => item.CreatedByUserId).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    private HttpClient CreateCommonUserClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "common");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "*:*:*");
        return client;
    }

    private async Task<int> CreateCustomerAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customer = new Customer
        {
            Name = $"幂等加固客户-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private async Task SeedExistingSpecAsync(
        int customerId,
        string project,
        string specification,
        string acceptance)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sourceFile = new WordFile
        {
            FileName = $"existing-{Guid.NewGuid():N}.xlsx",
            FileContent = [],
            FileHash = Guid.NewGuid().ToString("N"),
            FileType = UploadedFileType.ExcelXlsx,
            UploadedAt = DateTime.UtcNow,
            CreatedByUserId = 1,
            CompanyId = 1,
            OwnerOrgUnitId = 1
        };
        db.WordFiles.Add(sourceFile);
        await db.SaveChangesAsync();
        db.AcceptanceSpecs.Add(new AcceptanceSpec
        {
            CustomerId = customerId,
            Project = project,
            Specification = specification,
            Acceptance = acceptance,
            Remark = "旧备注",
            WordFileId = sourceFile.Id,
            CreatedByUserId = 1,
            OwnerOrgUnitId = 1,
            ImportedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static object CreateImportPayload(
        int fileId,
        int customerId,
        string executionRequestId,
        bool cleanupSourceFile) => new
    {
        executionRequestId,
        fileId,
        sheetIndex = 0,
        customerId,
        headerRowStart = 1,
        headerRowCount = 1,
        dataStartRow = 2,
        projectColumn = 1,
        specificationColumn = 2,
        acceptanceColumn = 3,
        remarkColumn = 4,
        cleanupSourceFile,
        duplicateCheckOptions = new
        {
            enableSemanticDuplicateCheck = false,
            enableLlmDuplicateReview = false
        }
    };

    private static Task<HttpResponseMessage> PostImportAsync(HttpClient client, object payload) =>
        client.PostAsync(
            "/api/documents/excel/import",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

    private static async Task<int> UploadExcelAsync(HttpClient client, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", $"idempotency-{Guid.NewGuid():N}.xlsx");

        using var response = await client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes(string project)
    {
        return CreateExcelBytes((project, $"{project}-规格", "验收", "备注"));
    }

    private static byte[] CreateExcelBytes(
        params (string Project, string Specification, string Acceptance, string Remark)[] rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Sheet1");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格内容";
        worksheet.Cell(1, 3).Value = "验收标准";
        worksheet.Cell(1, 4).Value = "备注";
        for (var index = 0; index < rows.Length; index++)
        {
            var rowNumber = index + 2;
            worksheet.Cell(rowNumber, 1).Value = rows[index].Project;
            worksheet.Cell(rowNumber, 2).Value = rows[index].Specification;
            worksheet.Cell(rowNumber, 3).Value = rows[index].Acceptance;
            worksheet.Cell(rowNumber, 4).Value = rows[index].Remark;
        }
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateEmptyExcelBytes()
    {
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("Sheet1");
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
