using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
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
    private readonly ApiWebApplicationFactory _factory;

    public ExcelFillFlowTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
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
        var previewAfterFillResp = await _client.GetAsync($"/api/documents/{fileId}/tables/0/preview?previewRows=500&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
        previewAfterFillResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewAfterFillJson = await previewAfterFillResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewAfterFillJson.Code.Should().Be(0);

        var rows = previewAfterFillJson.Data.GetProperty("rows");
        rows.GetArrayLength().Should().Be(2);
        rows[0][2].GetString().Should().Be("AC-1");
        rows[0][3].GetString().Should().Be("RM-1");
        rows[1][2].GetString().Should().Be("AC-2");
        rows[1][3].GetString().Should().Be("RM-2");

        await AssertLearnedColumnMappingRulesAsync(customerId, new[]
        {
            ("项目", ColumnMappingTargetField.Project),
            ("规格", ColumnMappingTargetField.Specification),
            ("验收", ColumnMappingTargetField.Acceptance),
            ("备注", ColumnMappingTargetField.Remark)
        });
    }

    [Fact]
    public async Task Execute_ManuallyConfirmedCurrentBest_ShouldWriteOverridesDespiteAiReject()
    {
        var originalXlsx = CreateExcelBytes(
        [
            ["项目", "规格", "验收", "备注"],
            ["装机前验机", "装机前验机", "", ""]
        ]);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(originalXlsx);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "manual-confirmed-ai-reject.xlsx");

        var uploadResp = await _client.PostAsync("/api/documents/upload", content);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();

        var suffix = Guid.NewGuid().ToString("N");
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = $"ManualConfirmedExcel-C-{suffix}" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = $"ManualConfirmedExcel-P-{suffix}" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var specResp = await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "设备安装",
            specification = "装机前验机",
            acceptance = "历史验收标准",
            remark = "历史备注"
        }));
        specResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var specJson = await specResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var specId = specJson.Data.GetProperty("id").GetInt32();

        var config = new
        {
            minScoreThreshold = 0.0,
            highConfidenceThreshold = 1.0,
            useLlmEntityResolution = false
        };
        var previewResp = await _client.PostAsync("/api/matching/batch-preview", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId,
            processId,
            config,
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
        var bestMatch = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0]
            .GetProperty("bestMatch");
        bestMatch.GetProperty("specId").GetInt32().Should().Be(specId);
        bestMatch.GetProperty("decision").GetString().Should().Be("manualReview");
        bestMatch.GetProperty("llmEquivalence").GetProperty("verdict").GetString()
            .Should().Be("different");

        var execResp = await _client.PostAsync("/api/matching/batch-execute", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId,
            processId,
            config,
            tables = new[]
            {
                new
                {
                    tableIndex = 0,
                    projectColumnIndex = 0,
                    specificationColumnIndex = 1,
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3,
                    mappings = new[]
                    {
                        new
                        {
                            rowIndex = 1,
                            specId,
                            manualConfirmed = true,
                            overrideAcceptance = "业务回复11",
                            overrideRemark = "业务回复22"
                        }
                    }
                }
            }
        }));
        execResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Data.GetProperty("filledCount").GetInt32().Should().Be(1);
        execJson.Data.GetProperty("skippedCount").GetInt32().Should().Be(0);
        var taskId = execJson.Data.GetProperty("taskId").GetString();
        taskId.Should().NotBeNullOrWhiteSpace();

        var downloadResp = await _client.GetAsync($"/api/matching/download/{taskId}");
        downloadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var downloadStream = await downloadResp.Content.ReadAsStreamAsync();
        using var downloadedWorkbook = new XLWorkbook(downloadStream);
        var downloadedRow = downloadedWorkbook.Worksheet(1).Row(2);
        downloadedRow.Cell(3).GetString().Should().Be("业务回复11");
        downloadedRow.Cell(4).GetString().Should().Be("业务回复22");

        var previewAfterFillResp = await _client.GetAsync(
            $"/api/documents/{fileId}/tables/0/preview?previewRows=500&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
        previewAfterFillResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewAfterFillJson = await previewAfterFillResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var filledRow = previewAfterFillJson.Data.GetProperty("rows")[0];
        filledRow[2].GetString().Should().Be("业务回复11");
        filledRow[3].GetString().Should().Be("业务回复22");
    }

    [Fact]
    public async Task MultiRegionExcel_WithDifferentTargetColumns_ShouldWriteEachRegionToItsOwnColumns()
    {
        var originalXlsx = CreateExcelBytes(
        [
            ["项目", "规格", "验收", "", "备注"],
            ["P1", "S1", "", "", ""],
            ["", "", "", "", ""],
            ["细项", "规格", "", "判定", "备注"],
            ["P2", "S2", "", "", ""]
        ]);
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(originalXlsx);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "multi-region-target-columns.xlsx");
        var upload = await _client.PostAsync("/api/documents/upload", content);
        var uploadBody = await upload.ReadAsAsync<ApiResponse<JsonElement>>();
        var fileId = uploadBody.Data.GetProperty("fileId").GetInt32();

        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = $"ExcelMultiRegion-C-{Guid.NewGuid():N}" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = $"ExcelMultiRegion-P-{Guid.NewGuid():N}" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        foreach (var source in new[] { (Project: "P1", Specification: "S1", Acceptance: "AC-1"), (Project: "P2", Specification: "S2", Acceptance: "AC-2") })
        {
            var createSpec = await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = source.Project,
                specification = source.Specification,
                acceptance = source.Acceptance,
                remark = ""
            }));
            createSpec.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var regions = new[]
        {
            new { regionId = "table-0-region-0", regionIndex = 0, projectColumnIndex = 0, specificationColumnIndex = 1, acceptanceColumnIndex = 2, remarkColumnIndex = (int?)4, headerRowStart = 1, headerRowCount = 1, dataStartRow = 2, dataEndRow = 2 },
            new { regionId = "table-0-region-1", regionIndex = 1, projectColumnIndex = 0, specificationColumnIndex = 1, acceptanceColumnIndex = 3, remarkColumnIndex = (int?)4, headerRowStart = 4, headerRowCount = 1, dataStartRow = 5, dataEndRow = 5 }
        };
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
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 4,
                    regions
                }
            }
        }));
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewBody = await preview.ReadAsAsync<ApiResponse<JsonElement>>();
        var items = previewBody.Data.GetProperty("tables")[0].GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("regionId").GetString().Should().Be("table-0-region-0");
        items[1].GetProperty("regionId").GetString().Should().Be("table-0-region-1");
        var mappings = items.EnumerateArray().Select(item => new
        {
            rowIndex = item.GetProperty("rowIndex").GetInt32(),
            specId = item.GetProperty("bestMatch").GetProperty("specId").GetInt32()
        }).ToArray();
        var previewTables = new[]
        {
            new
            {
                tableIndex = 0,
                items = items.EnumerateArray().Select(item => new
                {
                    regionId = item.GetProperty("regionId").GetString(),
                    regionIndex = item.GetProperty("regionIndex").GetInt32(),
                    acceptanceColumnIndex = item.GetProperty("acceptanceColumnIndex").GetInt32(),
                    remarkColumnIndex = item.TryGetProperty("remarkColumnIndex", out var remarkColumn) && remarkColumn.ValueKind != JsonValueKind.Null
                        ? remarkColumn.GetInt32()
                        : (int?)null,
                    rowIndex = item.GetProperty("rowIndex").GetInt32(),
                    sourceProject = item.GetProperty("sourceProject").GetString(),
                    sourceSpecification = item.GetProperty("sourceSpecification").GetString(),
                    bestMatch = JsonSerializer.Deserialize<JsonElement>(item.GetProperty("bestMatch").GetRawText()),
                    hasMatch = item.GetProperty("hasMatch").GetBoolean(),
                    confidenceLevel = item.GetProperty("confidenceLevel").GetString()
                }).ToArray()
            }
        };

        var executionRequestId = Guid.NewGuid().ToString("N");
        var executePayload = new
        {
            executionRequestId,
            fileId,
            customerId,
            processId,
            previewTables,
            config = new { minScoreThreshold = 0.0, highConfidenceThreshold = 0.95 },
            tables = new[]
            {
                new
                {
                    tableIndex = 0,
                    projectColumnIndex = 0,
                    specificationColumnIndex = 1,
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 4,
                    regions,
                    mappings
                }
            }
        };
        var executeTask = _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(executePayload));
        var concurrentRetryTask = _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(executePayload));
        await Task.WhenAll(executeTask, concurrentRetryTask);
        var execute = await executeTask;
        var concurrentRetry = await concurrentRetryTask;
        execute.StatusCode.Should().Be(HttpStatusCode.OK);
        concurrentRetry.StatusCode.Should().Be(HttpStatusCode.OK);
        var executeBody = await execute.ReadAsAsync<ApiResponse<JsonElement>>();
        var persistedTaskId = executeBody.Data.GetProperty("taskId").GetString();
        persistedTaskId.Should().MatchRegex("^[a-f0-9]{32}$");
        var concurrentRetryBody = await concurrentRetry.ReadAsAsync<ApiResponse<JsonElement>>();
        concurrentRetryBody.Data.GetProperty("taskId").GetString()
            .Should().Be(executeBody.Data.GetProperty("taskId").GetString());

        var retry = await _client.PostAsync("/api/matching/batch-execute", ApiClientJson.ToJsonContent(executePayload));
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        var retryBody = await retry.ReadAsAsync<ApiResponse<JsonElement>>();
        retryBody.Data.GetProperty("taskId").GetString()
            .Should().Be(executeBody.Data.GetProperty("taskId").GetString());

        var changedPayloadRetry = await _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                executionRequestId,
                fileId,
                customerId,
                processId,
                previewTables,
                config = new { minScoreThreshold = 0.0, highConfidenceThreshold = 0.94 },
                tables = executePayload.tables
            }));
        changedPayloadRetry.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var changedPayloadBody = await changedPayloadRetry.ReadAsAsync<ApiResponse<JsonElement>>();
        changedPayloadBody.Code.Should().Be(409);
        changedPayloadBody.Message.Should().Contain("不同的填充请求");

        var after = await _client.GetAsync(
            $"/api/documents/{fileId}/tables/0/preview?previewRows=500&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
        var afterBody = await after.ReadAsAsync<ApiResponse<JsonElement>>();
        var rows = afterBody.Data.GetProperty("rows");
        rows[0][2].GetString().Should().Be("AC-1");
        rows[3][2].GetString().Should().BeEmpty();
        rows[3][3].GetString().Should().Be("AC-2");

        await AssertLearnedColumnMappingRulesAsync(customerId, new[]
        {
            ("项目", ColumnMappingTargetField.Project),
            ("细项", ColumnMappingTargetField.Project),
            ("验收", ColumnMappingTargetField.Acceptance),
            ("判定", ColumnMappingTargetField.Acceptance)
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var historyRecord = await db.ExecutionHistoryRecords.SingleAsync(
            item => item.TaskId == persistedTaskId);
        var historyResponse = await _client.GetAsync($"/api/execution-history/{historyRecord.Id}");
        var historyBody = await historyResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var historyRows = historyBody.Data
            .GetProperty("smartFillPlayback")
            .GetProperty("files")[0]
            .GetProperty("sheets")[0]
            .GetProperty("rows");
        historyRows[0].GetProperty("regionId").GetString().Should().Be("table-0-region-0");
        historyRows[1].GetProperty("regionId").GetString().Should().Be("table-0-region-1");
        historyRows[1].GetProperty("acceptanceColumnIndex").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task ConcurrentExecute_SameRequestIdForDifferentFiles_ShouldReturnOneSuccessAndOneConflict()
    {
        async Task<int> UploadAsync(string fileName)
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(CreateExcelBytes(
            [
                ["项目", "规格", "验收"],
                ["P1", "S1", ""]
            ]));
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            content.Add(fileContent, "file", fileName);
            var upload = await _client.PostAsync("/api/documents/upload", content);
            upload.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await upload.ReadAsAsync<ApiResponse<JsonElement>>())
                .Data.GetProperty("fileId").GetInt32();
        }

        var firstFileId = await UploadAsync("idempotency-first.xlsx");
        var secondFileId = await UploadAsync("idempotency-second.xlsx");
        var executionRequestId = Guid.NewGuid().ToString("N");

        object BuildPayload(int fileId) => new
        {
            executionRequestId,
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
                            overrideAcceptance = $"FILE-{fileId}"
                        }
                    }
                }
            }
        };

        var firstExecution = _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(BuildPayload(firstFileId)));
        var secondExecution = _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(BuildPayload(secondFileId)));
        await Task.WhenAll(firstExecution, secondExecution);

        var responses = new[] { await firstExecution, await secondExecution };
        responses.Count(response => response.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        var conflict = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        var conflictBody = await conflict.ReadAsAsync<ApiResponse<JsonElement>>();
        conflictBody.Code.Should().Be(409);
        conflictBody.Message.Should().Contain("其他文件");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var succeededTaskId = (await responses.Single(response => response.StatusCode == HttpStatusCode.OK)
                .ReadAsAsync<ApiResponse<JsonElement>>())
            .Data.GetProperty("taskId").GetString();
        (await db.MatchingFillTasks.CountAsync(item => item.TaskId == succeededTaskId))
            .Should().Be(1);
    }

    [Fact]
    public async Task BatchExecute_WithOverlappingRegions_ShouldRejectBeforeWriteBack()
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
                            new { regionId = "r0", regionIndex = 0, projectColumnIndex = 0, specificationColumnIndex = 1, acceptanceColumnIndex = 2, headerRowStart = 1, headerRowCount = 1, dataStartRow = 2, dataEndRow = 5 },
                            new { regionId = "r1", regionIndex = 1, projectColumnIndex = 0, specificationColumnIndex = 1, acceptanceColumnIndex = 3, headerRowStart = 4, headerRowCount = 1, dataStartRow = 5, dataEndRow = 8 }
                        }
                    }
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("不能重叠");
    }

    [Fact]
    public async Task BatchExecute_ManualFill_ShouldDeriveWriteTargetFromRegionsNotPreviewSnapshot()
    {
        var originalXlsx = CreateExcelBytes(
        [
            ["项目", "规格", "验收", "错误目标"],
            ["P1", "S1", "", ""]
        ]);
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(originalXlsx);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "canonical-region-target.xlsx");
        var upload = await _client.PostAsync("/api/documents/upload", content);
        var uploadBody = await upload.ReadAsAsync<ApiResponse<JsonElement>>();
        var fileId = uploadBody.Data.GetProperty("fileId").GetInt32();

        var execute = await _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                previewTables = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        items = new[]
                        {
                            new
                            {
                                regionId = "r0",
                                regionIndex = 0,
                                acceptanceColumnIndex = 3,
                                rowIndex = 1,
                                sourceProject = "P1",
                                sourceSpecification = "S1"
                            }
                        }
                    }
                },
                tables = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        projectColumnIndex = 0,
                        specificationColumnIndex = 1,
                        acceptanceColumnIndex = 2,
                        regions = new[]
                        {
                            new { regionId = "r0", regionIndex = 0, projectColumnIndex = 0, specificationColumnIndex = 1, acceptanceColumnIndex = 2, headerRowStart = 1, headerRowCount = 1, dataStartRow = 2, dataEndRow = 2 }
                        },
                        mappings = new[]
                        {
                            new { rowIndex = 1, manualFill = true, overrideAcceptance = "MANUAL" }
                        }
                    }
                }
            }));

        execute.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await _client.GetAsync(
            $"/api/documents/{fileId}/tables/0/preview?previewRows=500&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
        var afterBody = await after.ReadAsAsync<ApiResponse<JsonElement>>();
        var row = afterBody.Data.GetProperty("rows")[0];
        row[2].GetString().Should().Be("MANUAL");
        row[3].GetString().Should().BeEmpty();
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

        var previewAfterFillResp = await _client.GetAsync($"/api/documents/{fileId}/tables/0/preview?previewRows=500&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
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

    private async Task AssertLearnedColumnMappingRulesAsync(
        int customerId,
        IEnumerable<(string Pattern, ColumnMappingTargetField TargetField)> expectedRules)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var (pattern, targetField) in expectedRules)
        {
            var rule = await db.ColumnMappingRules.SingleOrDefaultAsync(item =>
                item.CustomerId == customerId &&
                item.Pattern == pattern &&
                item.TargetField == targetField);

            rule.Should().NotBeNull();
            rule!.Source.Should().Be(ColumnMappingRuleSource.Learned);
            rule.MatchMode.Should().Be(ColumnMappingMatchMode.Equals);
            rule.Priority.Should().BeGreaterThanOrEqualTo(100);
            rule.Enabled.Should().BeTrue();
        }
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
        var previewAfterFillResp = await _client.GetAsync($"/api/documents/{fileId}/tables/0/preview?previewRows=500&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
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
        var previewAfterFillResp = await _client.GetAsync($"/api/documents/{fileId}/tables/0/preview?previewRows=500&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
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
        var previewAfterFillResp = await _client.GetAsync($"/api/documents/{fileId}/tables/0/preview?previewRows=500&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
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
        var previewAfterFillResp = await _client.GetAsync($"/api/documents/{fileId}/tables/0/preview?previewRows=500&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
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
                sp.GetRequiredService<IDocumentFileAccessService>(),
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
                sp.GetRequiredService<IFileStorageService>(),
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
    public ISmartStructureRoutingRuleRepository SmartStructureRoutingRules => _inner.SmartStructureRoutingRules;
    public IDocumentTemplateRepository DocumentTemplates => _inner.DocumentTemplates;
    public ISystemUserRepository SystemUsers => _inner.SystemUsers;
    public IAuditLogRepository AuditLogs => _inner.AuditLogs;
    public IMatchingFillTaskRepository MatchingFillTasks => _inner.MatchingFillTasks;
    public IExecutionHistoryRecordRepository ExecutionHistoryRecords => _inner.ExecutionHistoryRecords;
    public IOrgUnitRepository OrgUnits => _inner.OrgUnits;

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
    public ISmartStructureRoutingRuleRepository SmartStructureRoutingRules => _inner.SmartStructureRoutingRules;
    public IDocumentTemplateRepository DocumentTemplates => _inner.DocumentTemplates;
    public ISystemUserRepository SystemUsers => _inner.SystemUsers;
    public IAuditLogRepository AuditLogs => _inner.AuditLogs;
    public IMatchingFillTaskRepository MatchingFillTasks => _inner.MatchingFillTasks;
    public IExecutionHistoryRecordRepository ExecutionHistoryRecords => _inner.ExecutionHistoryRecords;
    public IOrgUnitRepository OrgUnits => _inner.OrgUnits;

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
