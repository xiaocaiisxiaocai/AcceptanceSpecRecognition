using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Repositories;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Api.Tests;

/// <summary>
/// Excel 智能填充端到端测试
/// </summary>
public class ExcelFillFlowTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ExcelFillFlowTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Upload_Preview_Execute_ForExcel_ShouldWriteBackToSourceFile()
    {
        // 1) 构造 Excel（项目/规格/验收/备注）
        var originalXlsx = CreateExcelBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" },
            new[] { "P2", "S2", "", "" }
        });

        // 2) 上传 Excel
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(originalXlsx);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "e2e.xlsx");

        var uploadResp = await _client.PostAsync("/api/documents/upload", content);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        uploadJson.Code.Should().Be(0);
        uploadJson.Data.GetProperty("fileType").GetInt32().Should().Be(1);
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();

        // 3) 准备匹配数据
        var customerId = (await (await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name = "ExcelFill-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processId = (await (await _client.PostAsync("/api/processes", ApiClientJson.ToJsonContent(new { name = "ExcelFill-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "P1",
            specification = "S1",
            acceptance = "AC-1",
            remark = "RM-1"
        }));
        await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "P2",
            specification = "S2",
            acceptance = "AC-2",
            remark = "RM-2"
        }));

        // 4) 匹配预览
        var previewResp = await _client.PostAsync("/api/matching/batch-preview", ApiClientJson.ToJsonContent(new
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
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3
                }
            }
        }));
        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);
        var items = previewJson.Data.GetProperty("tables")[0].GetProperty("items");
        items.GetArrayLength().Should().Be(2);

        var mappings = items.EnumerateArray().Select(i => new
        {
            rowIndex = i.GetProperty("rowIndex").GetInt32(),
            specId = i.GetProperty("bestMatch").GetProperty("specId").GetInt32()
        }).ToArray();

        // 5) 执行填充
        var execResp = await _client.PostAsync("/api/matching/batch-execute", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId,
            processId,
            config = new { minScoreThreshold = 0.0, highConfidenceThreshold = 0.95 },
            tables = new[]
            {
                new
                {
                    tableIndex = 0,
                    projectColumnIndex = 0,
                    specificationColumnIndex = 1,
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3,
                    mappings
                }
            }
        }));
        execResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(0);
        execJson.Data.GetProperty("filledCount").GetInt32().Should().Be(2);
        var taskId = execJson.Data.GetProperty("taskId").GetString();
        taskId.Should().NotBeNullOrWhiteSpace();
        execJson.Data.GetProperty("downloadUrl").GetString().Should().BeEmpty();

        // 6) 通过预览接口验证源 Excel 已被原地写回
        var previewAfterFillResp = await _client.GetAsync($"/api/documents/{fileId}/tables/0/preview?previewRows=0&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
        previewAfterFillResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewAfterFillJson = await previewAfterFillResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewAfterFillJson.Code.Should().Be(0);

        var rows = previewAfterFillJson.Data.GetProperty("rows");
        rows.GetArrayLength().Should().Be(2);
        rows[0][2].GetString().Should().Be("AC-1");
        rows[0][3].GetString().Should().Be("RM-1");
        rows[1][2].GetString().Should().Be("AC-2");
        rows[1][3].GetString().Should().Be("RM-2");
    }

    [Fact]
    public async Task Execute_ForExcel_WhenMatchedRemarkIsEmpty_ShouldClearOldRemarkCell()
    {
        var originalXlsx = CreateExcelBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "OLD-REMARK" }
        });

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(originalXlsx);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "clear-remark.xlsx");

        var uploadResp = await _client.PostAsync("/api/documents/upload", content);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();

        var customerId = (await (await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name = "ExcelClearRemark-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processId = (await (await _client.PostAsync("/api/processes", ApiClientJson.ToJsonContent(new { name = "ExcelClearRemark-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "P1",
            specification = "S1",
            acceptance = "AC-1",
            remark = (string?)null
        }));

        var previewResp = await _client.PostAsync("/api/matching/batch-preview", ApiClientJson.ToJsonContent(new
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
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3
                }
            }
        }));
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var mappings = previewJson.Data.GetProperty("tables")[0].GetProperty("items").EnumerateArray().Select(i => new
        {
            rowIndex = i.GetProperty("rowIndex").GetInt32(),
            specId = i.GetProperty("bestMatch").GetProperty("specId").GetInt32()
        }).ToArray();

        var execResp = await _client.PostAsync("/api/matching/batch-execute", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId,
            processId,
            config = new { minScoreThreshold = 0.0, highConfidenceThreshold = 0.95 },
            tables = new[]
            {
                new
                {
                    tableIndex = 0,
                    projectColumnIndex = 0,
                    specificationColumnIndex = 1,
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3,
                    mappings
                }
            }
        }));
        execResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewAfterFillResp = await _client.GetAsync($"/api/documents/{fileId}/tables/0/preview?previewRows=0&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
        var previewAfterFillJson = await previewAfterFillResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var rows = previewAfterFillJson.Data.GetProperty("rows");
        rows[0][2].GetString().Should().Be("AC-1");
        rows[0][3].GetString().Should().BeEmpty();
    }

    internal static byte[] CreateExcelBytes(string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                sheet.Cell(r + 1, c + 1).Value = rows[r][c] ?? string.Empty;
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class ExcelFillSnapshotFailureOrderTests : IClassFixture<FinalSaveFailureExcelApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FinalSaveFailureExcelApiWebApplicationFactory _factory;

    public ExcelFillSnapshotFailureOrderTests(FinalSaveFailureExcelApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Execute_ForExcel_WhenFinalSaveFails_ShouldNotPersistSourceWorkbook()
    {
        var fileId = await UploadExcelAsync(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" }
        });
        var (customerId, processId) = await CreateScopeAsync("ExcelSnapshotFail");
        await CreateSpecAsync(customerId, processId, "P1", "S1", "AC-1", "RM-1");

        var mappings = await PreviewMappingsAsync(fileId, customerId, processId);
        var execResp = await _client.PostAsync("/api/matching/batch-execute", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId,
            processId,
            config = new { minScoreThreshold = 0.0, highConfidenceThreshold = 0.95 },
            tables = new[]
            {
                new
                {
                    tableIndex = 0,
                    projectColumnIndex = 0,
                    specificationColumnIndex = 1,
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3,
                    mappings
                }
            }
        }));

        execResp.IsSuccessStatusCode.Should().BeFalse();
        var rows = await PreviewExcelRowsAsync(fileId);
        rows[0][2].GetString().Should().BeEmpty();
        rows[0][3].GetString().Should().BeEmpty();
        await AssertNoExecutionArtifactsPersistedAsync(fileId);
    }

    private async Task<int> UploadExcelAsync(string[][] rows)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ExcelFillFlowTests.CreateExcelBytes(rows));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "snapshot-fail.xlsx");
        var uploadResp = await _client.PostAsync("/api/documents/upload", content);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        return uploadJson.Data.GetProperty("fileId").GetInt32();
    }

    private async Task<(int CustomerId, int ProcessId)> CreateScopeAsync(string prefix)
    {
        var customerId = (await (await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name = $"{prefix}-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processId = (await (await _client.PostAsync("/api/processes", ApiClientJson.ToJsonContent(new { name = $"{prefix}-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        return (customerId, processId);
    }

    private Task<HttpResponseMessage> CreateSpecAsync(int customerId, int processId, string project, string specification, string acceptance, string remark)
    {
        return _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project,
            specification,
            acceptance,
            remark
        }));
    }

    private async Task<object[]> PreviewMappingsAsync(int fileId, int customerId, int processId)
    {
        var previewResp = await _client.PostAsync("/api/matching/batch-preview", ApiClientJson.ToJsonContent(new
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
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3
                }
            }
        }));
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        return previewJson.Data.GetProperty("tables")[0].GetProperty("items").EnumerateArray().Select(i => (object)new
        {
            rowIndex = i.GetProperty("rowIndex").GetInt32(),
            specId = i.GetProperty("bestMatch").GetProperty("specId").GetInt32()
        }).ToArray();
    }

    private async Task<JsonElement> PreviewExcelRowsAsync(int fileId)
    {
        var previewAfterFillResp = await _client.GetAsync($"/api/documents/{fileId}/tables/0/preview?previewRows=0&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
        var previewAfterFillJson = await previewAfterFillResp.ReadAsAsync<ApiResponse<JsonElement>>();
        return previewAfterFillJson.Data.GetProperty("rows");
    }

    private async Task AssertNoExecutionArtifactsPersistedAsync(int fileId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.MatchingFillTasks.CountAsync(item => item.SourceFileId == fileId)).Should().Be(0);
        (await db.ExecutionHistoryRecords.CountAsync(item => item.SourceFileId == fileId)).Should().Be(0);
    }
}

public class ExcelFillExecutionHistoryFailureOrderTests : IClassFixture<FinalSaveFailureExcelApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FinalSaveFailureExcelApiWebApplicationFactory _factory;

    public ExcelFillExecutionHistoryFailureOrderTests(FinalSaveFailureExcelApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Execute_ForExcel_WhenFinalSaveFails_ShouldNotPersistSourceWorkbook()
    {
        var fileId = await UploadExcelAsync(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" }
        });
        var (customerId, processId) = await CreateScopeAsync("ExcelHistoryFail");
        await CreateSpecAsync(customerId, processId, "P1", "S1", "AC-1", "RM-1");

        var mappings = await PreviewMappingsAsync(fileId, customerId, processId);
        var execResp = await _client.PostAsync("/api/matching/batch-execute", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId,
            processId,
            config = new { minScoreThreshold = 0.0, highConfidenceThreshold = 0.95 },
            tables = new[]
            {
                new
                {
                    tableIndex = 0,
                    projectColumnIndex = 0,
                    specificationColumnIndex = 1,
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3,
                    mappings
                }
            }
        }));

        execResp.IsSuccessStatusCode.Should().BeFalse();
        var rows = await PreviewExcelRowsAsync(fileId);
        rows[0][2].GetString().Should().BeEmpty();
        rows[0][3].GetString().Should().BeEmpty();
        await AssertNoExecutionArtifactsPersistedAsync(fileId);
    }

    private async Task<int> UploadExcelAsync(string[][] rows)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ExcelFillFlowTests.CreateExcelBytes(rows));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "history-fail.xlsx");
        var uploadResp = await _client.PostAsync("/api/documents/upload", content);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        return uploadJson.Data.GetProperty("fileId").GetInt32();
    }

    private async Task<(int CustomerId, int ProcessId)> CreateScopeAsync(string prefix)
    {
        var customerId = (await (await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name = $"{prefix}-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processId = (await (await _client.PostAsync("/api/processes", ApiClientJson.ToJsonContent(new { name = $"{prefix}-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        return (customerId, processId);
    }

    private Task<HttpResponseMessage> CreateSpecAsync(int customerId, int processId, string project, string specification, string acceptance, string remark)
    {
        return _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project,
            specification,
            acceptance,
            remark
        }));
    }

    private async Task<object[]> PreviewMappingsAsync(int fileId, int customerId, int processId)
    {
        var previewResp = await _client.PostAsync("/api/matching/batch-preview", ApiClientJson.ToJsonContent(new
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
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3
                }
            }
        }));
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        return previewJson.Data.GetProperty("tables")[0].GetProperty("items").EnumerateArray().Select(i => (object)new
        {
            rowIndex = i.GetProperty("rowIndex").GetInt32(),
            specId = i.GetProperty("bestMatch").GetProperty("specId").GetInt32()
        }).ToArray();
    }

    private async Task<JsonElement> PreviewExcelRowsAsync(int fileId)
    {
        var previewAfterFillResp = await _client.GetAsync($"/api/documents/{fileId}/tables/0/preview?previewRows=0&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
        var previewAfterFillJson = await previewAfterFillResp.ReadAsAsync<ApiResponse<JsonElement>>();
        return previewAfterFillJson.Data.GetProperty("rows");
    }

    private async Task AssertNoExecutionArtifactsPersistedAsync(int fileId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.MatchingFillTasks.CountAsync(item => item.SourceFileId == fileId)).Should().Be(0);
        (await db.ExecutionHistoryRecords.CountAsync(item => item.SourceFileId == fileId)).Should().Be(0);
    }
}

public class BatchExcelFillSnapshotFailureOrderTests : IClassFixture<FinalSaveFailureExcelApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FinalSaveFailureExcelApiWebApplicationFactory _factory;

    public BatchExcelFillSnapshotFailureOrderTests(FinalSaveFailureExcelApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task BatchExecute_ForExcel_WhenFinalSaveFails_ShouldNotPersistSourceWorkbookOrDatabaseArtifacts()
    {
        var fileId = await UploadExcelAsync(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" }
        });
        var (customerId, processId) = await CreateScopeAsync("BatchExcelSnapshotFail");
        await CreateSpecAsync(customerId, processId, "P1", "S1", "AC-1", "RM-1");

        var mappings = await PreviewMappingsAsync(fileId, customerId, processId);
        var execResp = await _client.PostAsync("/api/matching/batch-execute", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId,
            processId,
            config = new { minScoreThreshold = 0.0, highConfidenceThreshold = 0.95 },
            tables = new[]
            {
                new
                {
                    tableIndex = 0,
                    projectColumnIndex = 0,
                    specificationColumnIndex = 1,
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3,
                    mappings
                }
            }
        }));

        execResp.IsSuccessStatusCode.Should().BeFalse();
        var rows = await PreviewExcelRowsAsync(fileId);
        rows[0][2].GetString().Should().BeEmpty();
        rows[0][3].GetString().Should().BeEmpty();
        await AssertNoExecutionArtifactsPersistedAsync(fileId);
    }

    private async Task<int> UploadExcelAsync(string[][] rows)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ExcelFillFlowTests.CreateExcelBytes(rows));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "batch-snapshot-fail.xlsx");
        var uploadResp = await _client.PostAsync("/api/documents/upload", content);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        return uploadJson.Data.GetProperty("fileId").GetInt32();
    }

    private async Task<(int CustomerId, int ProcessId)> CreateScopeAsync(string prefix)
    {
        var customerId = (await (await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name = $"{prefix}-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processId = (await (await _client.PostAsync("/api/processes", ApiClientJson.ToJsonContent(new { name = $"{prefix}-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        return (customerId, processId);
    }

    private Task<HttpResponseMessage> CreateSpecAsync(int customerId, int processId, string project, string specification, string acceptance, string remark)
    {
        return _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project,
            specification,
            acceptance,
            remark
        }));
    }

    private async Task<object[]> PreviewMappingsAsync(int fileId, int customerId, int processId)
    {
        var previewResp = await _client.PostAsync("/api/matching/batch-preview", ApiClientJson.ToJsonContent(new
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
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3
                }
            }
        }));
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        return previewJson.Data.GetProperty("tables")[0].GetProperty("items").EnumerateArray().Select(i => (object)new
        {
            rowIndex = i.GetProperty("rowIndex").GetInt32(),
            specId = i.GetProperty("bestMatch").GetProperty("specId").GetInt32()
        }).ToArray();
    }

    private async Task<JsonElement> PreviewExcelRowsAsync(int fileId)
    {
        var previewAfterFillResp = await _client.GetAsync($"/api/documents/{fileId}/tables/0/preview?previewRows=0&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
        var previewAfterFillJson = await previewAfterFillResp.ReadAsAsync<ApiResponse<JsonElement>>();
        return previewAfterFillJson.Data.GetProperty("rows");
    }

    private async Task AssertNoExecutionArtifactsPersistedAsync(int fileId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.MatchingFillTasks.CountAsync(item => item.SourceFileId == fileId)).Should().Be(0);
        (await db.ExecutionHistoryRecords.CountAsync(item => item.SourceFileId == fileId)).Should().Be(0);
    }
}

public class BatchExcelFillExecutionHistoryFailureOrderTests : IClassFixture<FinalSaveFailureExcelApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FinalSaveFailureExcelApiWebApplicationFactory _factory;

    public BatchExcelFillExecutionHistoryFailureOrderTests(FinalSaveFailureExcelApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task BatchExecute_ForExcel_WhenFinalSaveFails_ShouldNotPersistSourceWorkbookOrDatabaseArtifacts()
    {
        var fileId = await UploadExcelAsync(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" }
        });
        var (customerId, processId) = await CreateScopeAsync("BatchExcelHistoryFail");
        await CreateSpecAsync(customerId, processId, "P1", "S1", "AC-1", "RM-1");

        var mappings = await PreviewMappingsAsync(fileId, customerId, processId);
        var execResp = await _client.PostAsync("/api/matching/batch-execute", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId,
            processId,
            config = new { minScoreThreshold = 0.0, highConfidenceThreshold = 0.95 },
            tables = new[]
            {
                new
                {
                    tableIndex = 0,
                    projectColumnIndex = 0,
                    specificationColumnIndex = 1,
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3,
                    mappings
                }
            }
        }));

        execResp.IsSuccessStatusCode.Should().BeFalse();
        var rows = await PreviewExcelRowsAsync(fileId);
        rows[0][2].GetString().Should().BeEmpty();
        rows[0][3].GetString().Should().BeEmpty();
        await AssertNoExecutionArtifactsPersistedAsync(fileId);
    }

    private async Task<int> UploadExcelAsync(string[][] rows)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ExcelFillFlowTests.CreateExcelBytes(rows));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "batch-history-fail.xlsx");
        var uploadResp = await _client.PostAsync("/api/documents/upload", content);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        return uploadJson.Data.GetProperty("fileId").GetInt32();
    }

    private async Task<(int CustomerId, int ProcessId)> CreateScopeAsync(string prefix)
    {
        var customerId = (await (await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name = $"{prefix}-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processId = (await (await _client.PostAsync("/api/processes", ApiClientJson.ToJsonContent(new { name = $"{prefix}-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        return (customerId, processId);
    }

    private Task<HttpResponseMessage> CreateSpecAsync(int customerId, int processId, string project, string specification, string acceptance, string remark)
    {
        return _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project,
            specification,
            acceptance,
            remark
        }));
    }

    private async Task<object[]> PreviewMappingsAsync(int fileId, int customerId, int processId)
    {
        var previewResp = await _client.PostAsync("/api/matching/batch-preview", ApiClientJson.ToJsonContent(new
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
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3
                }
            }
        }));
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        return previewJson.Data.GetProperty("tables")[0].GetProperty("items").EnumerateArray().Select(i => (object)new
        {
            rowIndex = i.GetProperty("rowIndex").GetInt32(),
            specId = i.GetProperty("bestMatch").GetProperty("specId").GetInt32()
        }).ToArray();
    }

    private async Task<JsonElement> PreviewExcelRowsAsync(int fileId)
    {
        var previewAfterFillResp = await _client.GetAsync($"/api/documents/{fileId}/tables/0/preview?previewRows=0&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
        var previewAfterFillJson = await previewAfterFillResp.ReadAsAsync<ApiResponse<JsonElement>>();
        return previewAfterFillJson.Data.GetProperty("rows");
    }

    private async Task AssertNoExecutionArtifactsPersistedAsync(int fileId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.MatchingFillTasks.CountAsync(item => item.SourceFileId == fileId)).Should().Be(0);
        (await db.ExecutionHistoryRecords.CountAsync(item => item.SourceFileId == fileId)).Should().Be(0);
    }
}

public sealed class SnapshotFailureExcelApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(MatchingTaskSnapshotService));
            services.AddScoped(sp => new MatchingTaskSnapshotService(
                new ThrowOnSaveChangesUnitOfWork(sp.GetRequiredService<IUnitOfWork>(), "模拟任务快照保存失败"),
                sp.GetRequiredService<IFileStorageService>(),
                sp.GetRequiredService<ILogger<MatchingTaskSnapshotService>>()));
        });
    }
}

public sealed class ExecutionHistoryFailureExcelApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(ExecutionHistoryAppService));
            services.AddScoped(sp => new ExecutionHistoryAppService(
                new ThrowOnSaveChangesUnitOfWork(sp.GetRequiredService<IUnitOfWork>(), "模拟执行历史保存失败"),
                sp.GetRequiredService<ILogger<ExecutionHistoryAppService>>()));
        });
    }
}

public sealed class FinalSaveFailureExcelApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IUnitOfWork));
            services.AddScoped<IUnitOfWork>(sp => new ThrowOnWorkflowFinalSaveUnitOfWork(
                new UnitOfWork(sp.GetRequiredService<AppDbContext>(), sp),
                "模拟最终提交失败"));
        });
    }
}

internal sealed class ThrowOnSaveChangesUnitOfWork : IUnitOfWork
{
    private readonly IUnitOfWork _inner;
    private readonly string _message;

    public ThrowOnSaveChangesUnitOfWork(IUnitOfWork inner, string message)
    {
        _inner = inner;
        _message = message;
    }

    public ICustomerRepository Customers => _inner.Customers;
    public IProcessRepository Processes => _inner.Processes;
    public IMachineModelRepository MachineModels => _inner.MachineModels;
    public IAcceptanceSpecRepository AcceptanceSpecs => _inner.AcceptanceSpecs;
    public IEmbeddingCacheRepository EmbeddingCaches => _inner.EmbeddingCaches;
    public IWordFileRepository WordFiles => _inner.WordFiles;
    public IAiServiceConfigRepository AiServiceConfigs => _inner.AiServiceConfigs;
    public IPromptTemplateRepository PromptTemplates => _inner.PromptTemplates;
    public IColumnMappingRuleRepository ColumnMappingRules => _inner.ColumnMappingRules;
    public ISystemUserRepository SystemUsers => _inner.SystemUsers;
    public IAuditLogRepository AuditLogs => _inner.AuditLogs;
    public IMatchingFillTaskRepository MatchingFillTasks => _inner.MatchingFillTasks;
    public IExecutionHistoryRecordRepository ExecutionHistoryRecords => _inner.ExecutionHistoryRecords;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(_message);
    }

    public int SaveChanges()
    {
        throw new InvalidOperationException(_message);
    }

    public Task BeginTransactionAsync() => _inner.BeginTransactionAsync();
    public Task CommitTransactionAsync() => _inner.CommitTransactionAsync();
    public Task RollbackTransactionAsync() => _inner.RollbackTransactionAsync();
    public void Dispose() => _inner.Dispose();
}

internal sealed class ThrowOnWorkflowFinalSaveUnitOfWork : IUnitOfWork
{
    private readonly IUnitOfWork _inner;
    private readonly string _message;

    public ThrowOnWorkflowFinalSaveUnitOfWork(IUnitOfWork inner, string message)
    {
        _inner = inner;
        _message = message;
    }

    public ICustomerRepository Customers => _inner.Customers;
    public IProcessRepository Processes => _inner.Processes;
    public IMachineModelRepository MachineModels => _inner.MachineModels;
    public IAcceptanceSpecRepository AcceptanceSpecs => _inner.AcceptanceSpecs;
    public IEmbeddingCacheRepository EmbeddingCaches => _inner.EmbeddingCaches;
    public IWordFileRepository WordFiles => _inner.WordFiles;
    public IAiServiceConfigRepository AiServiceConfigs => _inner.AiServiceConfigs;
    public IPromptTemplateRepository PromptTemplates => _inner.PromptTemplates;
    public IColumnMappingRuleRepository ColumnMappingRules => _inner.ColumnMappingRules;
    public ISystemUserRepository SystemUsers => _inner.SystemUsers;
    public IAuditLogRepository AuditLogs => _inner.AuditLogs;
    public IMatchingFillTaskRepository MatchingFillTasks => _inner.MatchingFillTasks;
    public IExecutionHistoryRecordRepository ExecutionHistoryRecords => _inner.ExecutionHistoryRecords;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (ShouldThrowForWorkflowFinalSave())
        {
            throw new InvalidOperationException(_message);
        }

        return _inner.SaveChangesAsync(cancellationToken);
    }

    public int SaveChanges()
    {
        if (ShouldThrowForWorkflowFinalSave())
        {
            throw new InvalidOperationException(_message);
        }

        return _inner.SaveChanges();
    }

    public Task BeginTransactionAsync() => _inner.BeginTransactionAsync();
    public Task CommitTransactionAsync() => _inner.CommitTransactionAsync();
    public Task RollbackTransactionAsync() => _inner.RollbackTransactionAsync();
    public void Dispose() => _inner.Dispose();

    private static bool ShouldThrowForWorkflowFinalSave()
    {
        return Environment.StackTrace.Contains("PersistExcelExecutionAsync", StringComparison.Ordinal);
    }
}
