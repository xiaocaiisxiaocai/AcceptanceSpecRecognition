using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Repositories;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class MatchingFillHardeningTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public MatchingFillHardeningTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task BatchExecute_LegacyManualFillOutsideSourceTable_ShouldBeRejected()
    {
        var fileId = await UploadExcelAsync(
        [
            ["项目", "规格", "验收"],
            ["P1", "S1", ""]
        ]);

        var response = await _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                tables = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        projectColumnIndex = 0,
                        specificationColumnIndex = 1,
                        acceptanceColumnIndex = 2,
                        mappings = new[]
                        {
                            new
                            {
                                rowIndex = 999,
                                manualFill = true,
                                overrideAcceptance = "FORGED"
                            }
                        }
                    }
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("不属于已确认的数据区域");
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    public async Task BatchExecute_InvalidHeaderBoundary_ShouldBeRejected(
        int headerRowCount,
        int dataStartRow)
    {
        var response = await _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId = 1,
                tables = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        projectColumnIndex = 0,
                        specificationColumnIndex = 1,
                        acceptanceColumnIndex = 2,
                        mappings = Array.Empty<object>(),
                        regions = new[]
                        {
                            new
                            {
                                regionId = "r0",
                                regionIndex = 0,
                                projectColumnIndex = 0,
                                specificationColumnIndex = 1,
                                acceptanceColumnIndex = 2,
                                headerRowStart = 1,
                                headerRowCount,
                                dataStartRow,
                                dataEndRow = 3
                            }
                        }
                    }
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<int> UploadExcelAsync(string[][] rows)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ExcelFillFlowTests.CreateExcelBytes(rows));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "matching-fill-hardening.xlsx");

        var response = await _client.PostAsync("/api/documents/upload", content);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return body.Data.GetProperty("fileId").GetInt32();
    }
}

public sealed class MatchingFillCommitCompensationTests : IClassFixture<CommitFailureApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CommitFailureApiWebApplicationFactory _factory;

    public MatchingFillCommitCompensationTests(CommitFailureApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task BatchExecute_WhenDatabaseCommitFails_ShouldRestoreWorkbookAndRollbackRecords()
    {
        var fileId = await UploadExcelAsync(
        [
            ["项目", "规格", "验收"],
            ["P1", "S1", ""]
        ]);
        var customerId = await CreateEntityAsync("/api/customers", "CommitCompensation-C");
        var processId = await CreateEntityAsync("/api/processes", "CommitCompensation-P");
        await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "P1",
            specification = "S1",
            acceptance = "AC-1"
        }));

        var preview = await _client.PostAsync("/api/matching/batch-preview", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId,
            processId,
            config = new { minScoreThreshold = 0.0 },
            tables = new[]
            {
                new
                {
                    tableIndex = 0,
                    projectColumnIndex = 0,
                    specificationColumnIndex = 1,
                    acceptanceColumnIndex = 2
                }
            }
        }));
        var previewBody = await preview.ReadAsAsync<ApiResponse<JsonElement>>();
        var item = previewBody.Data.GetProperty("tables")[0].GetProperty("items")[0];

        var execute = await _client.PostAsync("/api/matching/batch-execute", ApiClientJson.ToJsonContent(new
        {
            executionRequestId = Guid.NewGuid().ToString("N"),
            fileId,
            customerId,
            processId,
            config = new { minScoreThreshold = 0.0 },
            tables = new[]
            {
                new
                {
                    tableIndex = 0,
                    projectColumnIndex = 0,
                    specificationColumnIndex = 1,
                    acceptanceColumnIndex = 2,
                    mappings = new[]
                    {
                        new
                        {
                            rowIndex = item.GetProperty("rowIndex").GetInt32(),
                            specId = item.GetProperty("bestMatch").GetProperty("specId").GetInt32()
                        }
                    }
                }
            }
        }));

        execute.IsSuccessStatusCode.Should().BeFalse();
        var after = await _client.GetAsync(
            $"/api/documents/{fileId}/tables/0/preview?previewRows=500&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
        var afterBody = await after.ReadAsAsync<ApiResponse<JsonElement>>();
        afterBody.Data.GetProperty("rows")[0][2].GetString().Should().BeEmpty();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.MatchingFillTasks.CountAsync(item => item.SourceFileId == fileId)).Should().Be(0);
        (await db.ExecutionHistoryRecords.CountAsync(item => item.SourceFileId == fileId)).Should().Be(0);
    }

    private async Task<int> UploadExcelAsync(string[][] rows)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ExcelFillFlowTests.CreateExcelBytes(rows));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "commit-compensation.xlsx");
        var response = await _client.PostAsync("/api/documents/upload", content);
        return (await response.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("fileId").GetInt32();
    }

    private async Task<int> CreateEntityAsync(string url, string name)
    {
        var response = await _client.PostAsync(url, ApiClientJson.ToJsonContent(new { name }));
        return (await response.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
    }
}

public sealed class CommitFailureApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IUnitOfWork));
            services.AddScoped<IUnitOfWork>(serviceProvider =>
                new CommitFailureUnitOfWork(
                    new UnitOfWork(serviceProvider.GetRequiredService<AppDbContext>(), serviceProvider)));
        });
    }
}

public sealed class MatchingFillAmbiguousCommitTests : IClassFixture<AmbiguousCommitApiWebApplicationFactory>
{
    private readonly AmbiguousCommitApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public MatchingFillAmbiguousCommitTests(AmbiguousCommitApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task BatchExecute_WhenCommitSucceedsButReturnsError_ShouldKeepCommittedWorkbook()
    {
        using var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ExcelFillFlowTests.CreateExcelBytes(
        [
            ["项目", "规格", "验收"],
            ["P1", "S1", ""]
        ]));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        uploadContent.Add(fileContent, "file", "ambiguous-commit.xlsx");
        var upload = await _client.PostAsync("/api/documents/upload", uploadContent);
        var fileId = (await upload.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("fileId").GetInt32();

        var execute = await _client.PostAsync("/api/matching/batch-execute", ApiClientJson.ToJsonContent(new
        {
            executionRequestId = Guid.NewGuid().ToString("N"),
            fileId,
            tables = new[]
            {
                new
                {
                    tableIndex = 0,
                    projectColumnIndex = 0,
                    specificationColumnIndex = 1,
                    acceptanceColumnIndex = 2,
                    mappings = new[]
                    {
                        new
                        {
                            rowIndex = 1,
                            manualFill = true,
                            overrideAcceptance = "AC-COMMITTED"
                        }
                    }
                }
            }
        }));

        execute.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await _client.GetAsync(
            $"/api/documents/{fileId}/tables/0/preview?previewRows=500&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
        var afterBody = await after.ReadAsAsync<ApiResponse<JsonElement>>();
        afterBody.Data.GetProperty("rows")[0][2].GetString().Should().Be("AC-COMMITTED");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.MatchingFillTasks.SingleAsync(item => item.SourceFileId == fileId);
        task.PayloadJson.Should().Contain("\"fileMutationPending\":false");
        (await db.ExecutionHistoryRecords.CountAsync(item => item.SourceFileId == fileId)).Should().Be(1);
    }
}

public sealed class MatchingWordFillAmbiguousCommitTests : IClassFixture<WordAmbiguousCommitApiWebApplicationFactory>
{
    private readonly WordAmbiguousCommitApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public MatchingWordFillAmbiguousCommitTests(WordAmbiguousCommitApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task BatchExecuteWord_WhenCommitSucceedsButReturnsError_ShouldKeepCommittedArtifact()
    {
        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(
            new ByteArrayContent(CreateDocxBytes([
                ["项目", "规格", "验收"],
                ["P1", "S1", ""]
            ])),
            "file",
            "ambiguous-commit.docx");
        var upload = await _client.PostAsync("/api/documents/upload", uploadContent);
        upload.StatusCode.Should().Be(HttpStatusCode.OK);
        var fileId = (await upload.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("fileId").GetInt32();

        var execute = await _client.PostAsync("/api/matching/batch-execute", ApiClientJson.ToJsonContent(new
        {
            executionRequestId = Guid.NewGuid().ToString("N"),
            fileId,
            tables = new[]
            {
                new
                {
                    tableIndex = 0,
                    projectColumnIndex = 0,
                    specificationColumnIndex = 1,
                    acceptanceColumnIndex = 2,
                    mappings = new[]
                    {
                        new
                        {
                            rowIndex = 1,
                            manualFill = true,
                            overrideAcceptance = "WORD-COMMITTED"
                        }
                    }
                }
            }
        }));

        execute.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await execute.ReadAsAsync<ApiResponse<JsonElement>>();
        var taskId = response.Data.GetProperty("taskId").GetString();
        taskId.Should().NotBeNullOrWhiteSpace();
        (await _client.GetAsync($"/api/matching/download/{taskId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.MatchingFillTasks.CountAsync(item => item.SourceFileId == fileId)).Should().Be(1);
        (await db.ExecutionHistoryRecords.CountAsync(item => item.SourceFileId == fileId)).Should().Be(1);
    }

    private static byte[] CreateDocxBytes(string[][] rows)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(
                   stream,
                   WordprocessingDocumentType.Document,
                   true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var table = new Table();
            foreach (var row in rows)
            {
                table.AppendChild(new TableRow(row
                    .Select(value => new TableCell(new Paragraph(new Run(new Text(value ?? string.Empty)))))
                    .ToArray()));
            }

            main.Document.Body!.Append(table);
            main.Document.Save();
        }

        return stream.ToArray();
    }
}

public sealed class AmbiguousCommitApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IUnitOfWork));
            services.AddScoped<IUnitOfWork>(serviceProvider =>
                new AmbiguousCommitUnitOfWork(
                    new UnitOfWork(serviceProvider.GetRequiredService<AppDbContext>(), serviceProvider)));
        });
    }
}

public sealed class WordAmbiguousCommitApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IUnitOfWork));
            services.AddScoped<IUnitOfWork>(serviceProvider =>
                new AmbiguousCommitUnitOfWork(
                    new UnitOfWork(serviceProvider.GetRequiredService<AppDbContext>(), serviceProvider),
                    throwOnCommitCount: 1));
        });
    }
}

internal sealed class AmbiguousCommitUnitOfWork : IUnitOfWork
{
    private readonly IUnitOfWork _inner;
    private readonly int _throwOnCommitCount;
    private int _commitCount;

    public AmbiguousCommitUnitOfWork(IUnitOfWork inner, int throwOnCommitCount = 2)
    {
        _inner = inner;
        _throwOnCommitCount = throwOnCommitCount;
    }

    public ICustomerRepository Customers => _inner.Customers;
    public IProcessRepository Processes => _inner.Processes;
    public IMachineModelRepository MachineModels => _inner.MachineModels;
    public IAcceptanceSpecRepository AcceptanceSpecs => _inner.AcceptanceSpecs;
    public IAcceptanceSpecReferenceEventRepository AcceptanceSpecReferenceEvents => _inner.AcceptanceSpecReferenceEvents;
    public IEmbeddingCacheRepository EmbeddingCaches => _inner.EmbeddingCaches;
    public IWordFileRepository WordFiles => _inner.WordFiles;
    public IAiServiceConfigRepository AiServiceConfigs => _inner.AiServiceConfigs;
    public IPromptTemplateRepository PromptTemplates => _inner.PromptTemplates;
    public IColumnMappingRuleRepository ColumnMappingRules => _inner.ColumnMappingRules;
    public ISmartStructureRoutingRuleRepository SmartStructureRoutingRules => _inner.SmartStructureRoutingRules;
    public IDocumentTemplateRepository DocumentTemplates => _inner.DocumentTemplates;
    public ISystemUserRepository SystemUsers => _inner.SystemUsers;
    public IAuditLogRepository AuditLogs => _inner.AuditLogs;
    public IMatchingFillTaskRepository MatchingFillTasks => _inner.MatchingFillTasks;
    public IExecutionHistoryRecordRepository ExecutionHistoryRecords => _inner.ExecutionHistoryRecords;
    public IOrgUnitRepository OrgUnits => _inner.OrgUnits;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _inner.SaveChangesAsync(cancellationToken);
    public int SaveChanges() => _inner.SaveChanges();
    public Task BeginTransactionAsync() => _inner.BeginTransactionAsync();
    public async Task CommitTransactionAsync()
    {
        await _inner.CommitTransactionAsync();
        if (Interlocked.Increment(ref _commitCount) == _throwOnCommitCount)
        {
            throw new InvalidOperationException("模拟提交已成功但客户端收到连接错误");
        }
    }
    public Task RollbackTransactionAsync() => _inner.RollbackTransactionAsync();
    public void Dispose() => _inner.Dispose();
}

internal sealed class CommitFailureUnitOfWork : IUnitOfWork
{
    private readonly IUnitOfWork _inner;
    private int _commitCount;

    public CommitFailureUnitOfWork(IUnitOfWork inner) => _inner = inner;

    public ICustomerRepository Customers => _inner.Customers;
    public IProcessRepository Processes => _inner.Processes;
    public IMachineModelRepository MachineModels => _inner.MachineModels;
    public IAcceptanceSpecRepository AcceptanceSpecs => _inner.AcceptanceSpecs;
    public IAcceptanceSpecReferenceEventRepository AcceptanceSpecReferenceEvents => _inner.AcceptanceSpecReferenceEvents;
    public IEmbeddingCacheRepository EmbeddingCaches => _inner.EmbeddingCaches;
    public IWordFileRepository WordFiles => _inner.WordFiles;
    public IAiServiceConfigRepository AiServiceConfigs => _inner.AiServiceConfigs;
    public IPromptTemplateRepository PromptTemplates => _inner.PromptTemplates;
    public IColumnMappingRuleRepository ColumnMappingRules => _inner.ColumnMappingRules;
    public ISmartStructureRoutingRuleRepository SmartStructureRoutingRules => _inner.SmartStructureRoutingRules;
    public IDocumentTemplateRepository DocumentTemplates => _inner.DocumentTemplates;
    public ISystemUserRepository SystemUsers => _inner.SystemUsers;
    public IAuditLogRepository AuditLogs => _inner.AuditLogs;
    public IMatchingFillTaskRepository MatchingFillTasks => _inner.MatchingFillTasks;
    public IExecutionHistoryRecordRepository ExecutionHistoryRecords => _inner.ExecutionHistoryRecords;
    public IOrgUnitRepository OrgUnits => _inner.OrgUnits;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _inner.SaveChangesAsync(cancellationToken);
    public int SaveChanges() => _inner.SaveChanges();
    public Task BeginTransactionAsync() => _inner.BeginTransactionAsync();
    public Task CommitTransactionAsync()
    {
        if (Interlocked.Increment(ref _commitCount) == 2)
        {
            throw new InvalidOperationException("模拟最终事务提交失败");
        }
        return _inner.CommitTransactionAsync();
    }
    public Task RollbackTransactionAsync() => _inner.RollbackTransactionAsync();
    public void Dispose() => _inner.Dispose();
}
