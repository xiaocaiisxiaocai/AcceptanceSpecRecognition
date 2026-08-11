using System.Net;
using System.Net.Http.Headers;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

/// <summary>
/// 执行记录 API 集成测试
/// </summary>
public class ExecutionHistoryApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private int? _businessOrgUnitId;

    public ExecutionHistoryApiTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetExecutionHistory_WhenPageSizeIsUnbounded_ShouldReturnBoundedPageContract()
    {
        var response = await _client.GetAsync("/api/execution-history?page=1&pageSize=2147483647");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<PagedData<JsonElement>>>();
        body.Data!.PageSize.Should().Be(200);
        body.Data.Items.Should().HaveCountLessThanOrEqualTo(200);
    }

    [Fact]
    public async Task SmartFillExecute_ShouldPersistPlaybackSummary_AndExposeSmartFillPlaybackDetail()
    {
        var docxBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" },
            new[] { "P2", "S2", "", "" },
            new[] { "P3", "S3", "", "" },
            new[] { "P4", "S4", "", "" }
        });

        var fileId = await UploadDocumentAsync(docxBytes, "execution-history-smart-fill.docx");
        var customerId = await CreateCustomerAsync("ExecutionHistory-C1");
        var processId = await CreateProcessAsync("ExecutionHistory-P1");
        var specId = await CreateSpecAsync(customerId, processId, "P1", "S1", "AC-1", "RM-1");
        var specId2 = await CreateSpecAsync(customerId, processId, "P2", "S2", "AC-2", "RM-2");

        var executeResp = await _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                customerId,
                processId,
                config = new { highConfidenceThreshold = 0.95 },
                previewTables = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        items = new object[]
                        {
                            new
                            {
                                rowIndex = 1,
                                sourceProject = "P1",
                                sourceSpecification = "S1",
                                confidenceLevel = "high",
                                bestMatch = new
                                {
                                    specId,
                                    project = "P1",
                                    specification = "S1",
                                    acceptance = "AC-1",
                                    remark = "RM-1",
                                    score = 1.0,
                                    embeddingScore = 1.0,
                                    scoreDetails = new { exact = 1.0 },
                                    decision = "autoApply",
                                    selectionMode = "exactShortcut",
                                    selectionSummary = "项目与规格完全一致",
                                    matchBasis = "projectSpecification",
                                    evidenceSummary = new[] { "项目与规格完全一致" },
                                    conflictSummary = Array.Empty<string>(),
                                    issues = Array.Empty<object>(),
                                    entities = Array.Empty<object>(),
                                    topCandidates = new object[]
                                    {
                                        new
                                        {
                                            rank = 1,
                                            specId,
                                            project = "P1",
                                            specification = "S1",
                                            acceptance = "AC-1",
                                            remark = "RM-1",
                                            score = 1.0,
                                            embeddingScore = 1.0,
                                            scoreDetails = new { exact = 1.0 },
                                            decision = "autoApply",
                                            selectionMode = "exactShortcut",
                                            selectionSummary = "完全一致",
                                            matchBasis = "projectSpecification",
                                            evidenceSummary = new[] { "项目与规格完全一致" },
                                            conflictSummary = Array.Empty<string>(),
                                            issues = Array.Empty<object>(),
                                            entities = Array.Empty<object>()
                                        }
                                    },
                                    recalledCandidateCount = 1,
                                    isAmbiguous = false
                                }
                            },
                            new
                            {
                                rowIndex = 2,
                                sourceProject = "P2",
                                sourceSpecification = "S2",
                                confidenceLevel = "medium",
                                bestMatch = new
                                {
                                    specId = specId2,
                                    project = "P2",
                                    specification = "S2",
                                    acceptance = "AC-2",
                                    remark = "RM-2",
                                    score = 0.82,
                                    embeddingScore = 0.74,
                                    scoreDetails = new { embedding = 0.74, rerank = 0.82 },
                                    decision = "manualReview",
                                    selectionMode = "aiRerank",
                                    selectionSummary = "AI 复核后建议人工确认",
                                    matchBasis = "specification",
                                    evidenceSummary = new[] { "项目一致" },
                                    conflictSummary = new[] { "规格表述存在轻微差异" },
                                    issues = Array.Empty<object>(),
                                    entities = Array.Empty<object>(),
                                    reviewScore = 91.5,
                                    reviewReason = "复核判定语义等价",
                                    reviewCommentary = "仅格式差异，可人工确认采用",
                                    topCandidates = new object[]
                                    {
                                        new
                                        {
                                            rank = 1,
                                            specId = specId2,
                                            project = "P2",
                                            specification = "S2",
                                            acceptance = "AC-2",
                                            remark = "RM-2",
                                            score = 0.82,
                                            embeddingScore = 0.74,
                                            scoreDetails = new { embedding = 0.74, rerank = 0.82 },
                                            decision = "manualReview",
                                            selectionMode = "aiRerank",
                                            selectionSummary = "AI 复核保留此候选",
                                            matchBasis = "specification",
                                            evidenceSummary = new[] { "项目一致" },
                                            conflictSummary = new[] { "规格表述存在轻微差异" },
                                            issues = Array.Empty<object>(),
                                            entities = Array.Empty<object>(),
                                            llmEquivalence = new
                                            {
                                                verdict = "equivalent",
                                                reasonType = "equivalent_expression",
                                                reason = "语义等价",
                                                confidence = 0.91
                                            }
                                        }
                                    },
                                    recalledCandidateCount = 3,
                                    isAmbiguous = true,
                                    scoreGap = 0.06,
                                    rerankSummary = "AI 认为该候选最接近",
                                    llmEquivalence = new
                                    {
                                        verdict = "equivalent",
                                        reasonType = "equivalent_expression",
                                        reason = "语义等价",
                                        confidence = 0.91
                                    }
                                }
                            },
                            new
                            {
                                rowIndex = 3,
                                sourceProject = "P3",
                                sourceSpecification = "S3",
                                confidenceLevel = "none",
                                noMatchReason = "未找到可采用候选"
                            },
                            new
                            {
                                rowIndex = 4,
                                sourceProject = "P4",
                                sourceSpecification = "S4",
                                confidenceLevel = "none",
                                noMatchReason = "手工填写"
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
                        remarkColumnIndex = 3,
                        headerRowStart = 1,
                        headerRowCount = 1,
                        dataStartRow = 2,
                        dataEndRow = 5,
                        mappings = new object[]
                        {
                            new { rowIndex = 1, specId },
                            new
                            {
                                rowIndex = 2,
                                specId = specId2,
                                manualConfirmed = true,
                                overrideAcceptance = "AC-2-人工",
                                overrideRemark = "RM-2-人工"
                            },
                            new
                            {
                                rowIndex = 4,
                                manualFill = true,
                                overrideAcceptance = "AC-4-手工",
                                overrideRemark = "RM-4-手工"
                            }
                        }
                    }
                }
            }));

        executeResp.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await executeResp.Content.ReadAsStringAsync());
        var executeJson = await executeResp.ReadAsAsync<ApiResponse<JsonElement>>();
        executeJson.Code.Should().Be(0);
        var taskId = executeJson.Data.GetProperty("taskId").GetString();
        taskId.Should().NotBeNullOrWhiteSpace();

        var listResp = await _client.GetAsync("/api/execution-history?page=1&pageSize=20");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResp.ReadAsAsync<ApiResponse<JsonElement>>();
        listJson.Code.Should().Be(0);

        var items = listJson.Data.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0);
        var record = items.EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("taskId").GetString() == taskId);
        record.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        record.GetProperty("taskType").GetString().Should().Be("smart-fill");
        record.GetProperty("fileCount").GetInt32().Should().Be(1);
        record.GetProperty("totalRowCount").GetInt32().Should().Be(4);
        record.GetProperty("adoptedRowCount").GetInt32().Should().Be(3);
        record.GetProperty("unmatchedRowCount").GetInt32().Should().Be(1);
        record.GetProperty("smartFillSummary").GetProperty("exactMatchedRowCount").GetInt32().Should().Be(2);
        record.GetProperty("smartFillSummary").GetProperty("aiMatchedRowCount").GetInt32().Should().Be(0);
        record.GetProperty("smartFillSummary").GetProperty("manualConfirmedRowCount").GetInt32().Should().Be(1);
        record.GetProperty("smartFillSummary").GetProperty("manualEditedRowCount").GetInt32().Should().Be(2);
        record.GetProperty("smartFillSummary").GetProperty("notUsedRowCount").GetInt32().Should().Be(1);
        record.GetProperty("smartFillSummary").GetProperty("hasPlaybackArchive").GetBoolean().Should().BeTrue();

        var detailId = record.GetProperty("id").GetInt32();
        var detailResp = await _client.GetAsync($"/api/execution-history/{detailId}");
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailJson = await detailResp.ReadAsAsync<ApiResponse<JsonElement>>();
        detailJson.Code.Should().Be(0);

        var playback = detailJson.Data.GetProperty("smartFillPlayback");
        playback.GetProperty("isLegacy").GetBoolean().Should().BeFalse();
        playback.GetProperty("payloadVersion").GetInt32().Should().BeGreaterThan(0);
        var files = playback.GetProperty("files");
        files.GetArrayLength().Should().Be(1);

        var rows = files[0].GetProperty("sheets")[0].GetProperty("rows");
        rows.GetArrayLength().Should().Be(4);

        rows[0].GetProperty("displayTags")[0].GetString().Should().Be("完全匹配");
        rows[0].GetProperty("previewSnapshot").GetProperty("bestMatch").GetProperty("selectionMode").GetString().Should().Be("exactShortcut");
        rows[0].GetProperty("previewSnapshot").GetProperty("bestMatch").GetProperty("topCandidates").GetArrayLength().Should().Be(1, "完整回放归档保留完全一致行的候选上下文，超大记录才由 Slimmer 精简");
        rows[0].GetProperty("executionSnapshot").GetProperty("finalAcceptance").GetString().Should().Be("AC-1");

        rows[1].GetProperty("matchOrigin").GetString().Should().Be("exact");
        rows[1].GetProperty("displayTags").EnumerateArray().Select(item => item.GetString()).Should().Equal("完全匹配", "人工确认", "人工写入");
        rows[1].GetProperty("previewSnapshot").GetProperty("bestMatch").GetProperty("selectionMode").GetString().Should().Be("exactShortcut");
        rows[1].GetProperty("previewSnapshot").GetProperty("bestMatch").GetProperty("matchBasis").GetString().Should().Be("projectSpecification");
        rows[1].GetProperty("previewSnapshot").GetProperty("bestMatch").GetProperty("reviewScore").ValueKind.Should().Be(JsonValueKind.Null);
        rows[1].GetProperty("previewSnapshot").GetProperty("bestMatch").GetProperty("reviewReason").ValueKind.Should().Be(JsonValueKind.Null);
        rows[1].GetProperty("previewSnapshot").GetProperty("bestMatch").GetProperty("reviewCommentary").ValueKind.Should().Be(JsonValueKind.Null);
        rows[1].GetProperty("previewSnapshot").GetProperty("bestMatch").GetProperty("topCandidates").GetArrayLength().Should().Be(1, "非完全一致的归档仍需保留候选上下文");
        rows[1].GetProperty("previewSnapshot").GetProperty("bestMatch").GetProperty("topCandidates")[0].GetProperty("matchBasis").GetString().Should().Be("projectSpecification");
        rows[1].GetProperty("executionSnapshot").GetProperty("manualConfirmed").GetBoolean().Should().BeTrue();
        rows[1].GetProperty("executionSnapshot").GetProperty("manualEdited").GetBoolean().Should().BeTrue();
        rows[1].GetProperty("executionSnapshot").GetProperty("finalAcceptance").GetString().Should().Be("AC-2-人工");
        rows[1].GetProperty("executionSnapshot").GetProperty("finalRemark").GetString().Should().Be("RM-2-人工");

        rows[2].GetProperty("displayTags").EnumerateArray().Select(item => item.GetString()).Should().Equal("未采用/未匹配");
        rows[2].GetProperty("previewSnapshot").GetProperty("noMatchReason").GetString().Should().Be("执行时未匹配到可用规格");
        rows[2].GetProperty("executionSnapshot").GetProperty("status").GetString().Should().Be("unmatched");

        rows[3].GetProperty("status").GetString().Should().Be("adopted");
        rows[3].GetProperty("matchOrigin").GetString().Should().Be("none");
        rows[3].GetProperty("displayTags").EnumerateArray().Select(item => item.GetString()).Should().Equal("人工写入");
        rows[3].GetProperty("executionSnapshot").GetProperty("selectedSpecId").ValueKind.Should().Be(JsonValueKind.Null);
        rows[3].GetProperty("executionSnapshot").GetProperty("finalAcceptance").GetString().Should().Be("AC-4-手工");
        rows[3].GetProperty("executionSnapshot").GetProperty("finalRemark").GetString().Should().Be("RM-4-手工");
        rows[3].GetProperty("executionSnapshot").GetProperty("manualEdited").GetBoolean().Should().BeTrue();
        rows[3].GetProperty("executionSnapshot").GetProperty("status").GetString().Should().Be("adopted");

        var archiveListResp = await _client.GetAsync(
            "/api/execution-history/smart-fill-archives?page=1&pageSize=20&keyword=execution-history-smart-fill");
        archiveListResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var archiveList = await archiveListResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var archiveRecord = archiveList.Data.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("taskId").GetString() == taskId);
        archiveRecord.GetProperty("hasResultArchive").GetBoolean().Should().BeTrue();
        archiveRecord.GetProperty("resultFileName").GetString().Should().Be("execution-history-smart-fill.docx");

        using var archiveDownloadResp = await _client.GetAsync(
            $"/api/execution-history/smart-fill-archives/{archiveRecord.GetProperty("id").GetInt32()}/download");
        archiveDownloadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        archiveDownloadResp.Content.Headers.ContentType!.MediaType.Should().Be(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        var archiveBytes = await archiveDownloadResp.Content.ReadAsByteArrayAsync();
        archiveBytes.Should().NotBeEmpty();
        using var archiveStream = new MemoryStream(archiveBytes);
        using var archiveDocument = WordprocessingDocument.Open(archiveStream, false);
        archiveDocument.MainDocumentPart!.Document!.InnerText.Should().Contain("AC-1");
    }

    [Fact]
    public async Task SmartFillExecute_WhenManualFillRowIsFilteredFromSourceRows_ShouldStillPersistHistoryRow()
    {
        var docxBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" },
            new[] { "", "", "", "" }
        });

        var fileId = await UploadDocumentAsync(docxBytes, "execution-history-smart-fill-filtered-manual.docx");
        var customerId = await CreateCustomerAsync("ExecutionHistory-Filtered-C1");
        var processId = await CreateProcessAsync("ExecutionHistory-Filtered-P1");
        var specId = await CreateSpecAsync(customerId, processId, "P1", "S1", "AC-1", "RM-1");

        var executeResp = await _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                customerId,
                processId,
                config = new { highConfidenceThreshold = 0.95 },
                previewTables = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        items = new object[]
                        {
                            new
                            {
                                rowIndex = 1,
                                sourceProject = "P1",
                                sourceSpecification = "S1",
                                confidenceLevel = "high",
                                bestMatch = new
                                {
                                    specId,
                                    project = "P1",
                                    specification = "S1",
                                    acceptance = "AC-1",
                                    remark = "RM-1",
                                    score = 1.0,
                                    embeddingScore = 1.0,
                                    scoreDetails = new { exact = 1.0 },
                                    decision = "autoApply",
                                    selectionMode = "exactShortcut",
                                    selectionSummary = "项目与规格完全一致",
                                    matchBasis = "projectSpecification",
                                    evidenceSummary = new[] { "项目与规格完全一致" },
                                    conflictSummary = Array.Empty<string>(),
                                    issues = Array.Empty<object>(),
                                    entities = Array.Empty<object>(),
                                    recalledCandidateCount = 1,
                                    isAmbiguous = false
                                }
                            },
                            new
                            {
                                rowIndex = 2,
                                sourceProject = "",
                                sourceSpecification = "",
                                confidenceLevel = "none",
                                noMatchReason = "手工填写"
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
                        remarkColumnIndex = 3,
                        filterEmptySourceRows = true,
                        mappings = new object[]
                        {
                            new { rowIndex = 1, specId },
                            new
                            {
                                rowIndex = 2,
                                manualFill = true,
                                overrideAcceptance = "空源行手工验收",
                                overrideRemark = "空源行手工备注"
                            }
                        }
                    }
                }
            }));

        executeResp.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await executeResp.Content.ReadAsStringAsync());
        var executeJson = await executeResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var taskId = executeJson.Data.GetProperty("taskId").GetString();

        var listResp = await _client.GetAsync("/api/execution-history?page=1&pageSize=20");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var record = listJson.Data.GetProperty("items").EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("taskId").GetString() == taskId);
        record.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        record.GetProperty("totalRowCount").GetInt32().Should().Be(2);
        record.GetProperty("adoptedRowCount").GetInt32().Should().Be(2);

        var detailResp = await _client.GetAsync($"/api/execution-history/{record.GetProperty("id").GetInt32()}");
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailJson = await detailResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var rows = detailJson.Data.GetProperty("smartFillPlayback").GetProperty("files")[0].GetProperty("sheets")[0].GetProperty("rows");

        rows.GetArrayLength().Should().Be(2);
        var manualRow = rows.EnumerateArray()
            .Single(row => row.GetProperty("rowIndex").GetInt32() == 2);
        manualRow.GetProperty("status").GetString().Should().Be("adopted");
        manualRow.GetProperty("executionSnapshot").GetProperty("finalAcceptance").GetString().Should().Be("空源行手工验收");
        manualRow.GetProperty("executionSnapshot").GetProperty("finalRemark").GetString().Should().Be("空源行手工备注");
    }

    [Fact]
    public async Task SmartFillExecute_WhenTableFilterIsMissing_ShouldUseGlobalFilterEmptySourceRowsForHistory()
    {
        var docxBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" },
            new[] { "", "", "", "" }
        });

        var fileId = await UploadDocumentAsync(docxBytes, "execution-history-smart-fill-global-filter.docx");
        var customerId = await CreateCustomerAsync("ExecutionHistory-GlobalFilter-C1");
        var processId = await CreateProcessAsync("ExecutionHistory-GlobalFilter-P1");
        var specId = await CreateSpecAsync(customerId, processId, "P1", "S1", "AC-1", "RM-1");

        var executeResp = await _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                customerId,
                processId,
                config = new { highConfidenceThreshold = 0.95, filterEmptySourceRows = false },
                previewTables = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        items = new object[]
                        {
                            new
                            {
                                rowIndex = 1,
                                sourceProject = "P1",
                                sourceSpecification = "S1",
                                confidenceLevel = "high",
                                bestMatch = new
                                {
                                    specId,
                                    project = "P1",
                                    specification = "S1",
                                    acceptance = "AC-1",
                                    remark = "RM-1",
                                    score = 1.0,
                                    embeddingScore = 1.0,
                                    scoreDetails = new { exact = 1.0 },
                                    decision = "autoApply",
                                    selectionMode = "exactShortcut",
                                    selectionSummary = "项目与规格完全一致",
                                    matchBasis = "projectSpecification",
                                    evidenceSummary = new[] { "项目与规格完全一致" },
                                    conflictSummary = Array.Empty<string>(),
                                    issues = Array.Empty<object>(),
                                    entities = Array.Empty<object>(),
                                    recalledCandidateCount = 1,
                                    isAmbiguous = false
                                }
                            },
                            new
                            {
                                rowIndex = 2,
                                sourceProject = "",
                                sourceSpecification = "",
                                confidenceLevel = "none",
                                noMatchReason = "空源行"
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
                        remarkColumnIndex = 3,
                        mappings = new object[]
                        {
                            new { rowIndex = 1, specId }
                        }
                    }
                }
            }));

        executeResp.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await executeResp.Content.ReadAsStringAsync());
        var executeJson = await executeResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var taskId = executeJson.Data.GetProperty("taskId").GetString();

        var listResp = await _client.GetAsync("/api/execution-history?page=1&pageSize=20");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var record = listJson.Data.GetProperty("items").EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("taskId").GetString() == taskId);
        record.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        record.GetProperty("totalRowCount").GetInt32().Should().Be(2);
        record.GetProperty("adoptedRowCount").GetInt32().Should().Be(1);
        record.GetProperty("unmatchedRowCount").GetInt32().Should().Be(1);

        var detailResp = await _client.GetAsync($"/api/execution-history/{record.GetProperty("id").GetInt32()}");
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailJson = await detailResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var rows = detailJson.Data.GetProperty("smartFillPlayback").GetProperty("files")[0].GetProperty("sheets")[0].GetProperty("rows");

        rows.GetArrayLength().Should().Be(2);
        var emptyRow = rows.EnumerateArray()
            .Single(row => row.GetProperty("rowIndex").GetInt32() == 2);
        emptyRow.GetProperty("sourceProject").GetString().Should().Be("");
        emptyRow.GetProperty("sourceSpecification").GetString().Should().Be("");
        emptyRow.GetProperty("status").GetString().Should().Be("unmatched");
    }

    [Fact]
    public async Task BatchReplyExecute_ShouldPersistExecutionHistory_WithFilesAndSheetRows()
    {
        var sessionId = await UploadBatchReplySourceAsync(
            CreateDocxBytes(new[]
            {
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "P1", "S1", "AC-1", "RM-1" }
            }),
            "execution-history-batch-reply-source.docx");

        using (var previewContent = new MultipartFormDataContent
        {
            { new StringContent(sessionId), "sessionId" },
            { new StringContent("""[{"tableIndex":0,"projectColumnIndex":0,"specificationColumnIndex":1,"acceptanceColumnIndex":2,"remarkColumnIndex":3,"filterEmptySourceRows":true}]"""), "tableConfigsJson" }
        })
        {
            previewContent.Add(CreateTargetFileContent(CreateDocxBytes(new[]
            {
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "P1", "S1", "", "" }
            }), "execution-history-batch-reply-target-a.docx"), "targetFiles", "execution-history-batch-reply-target-a.docx");

            previewContent.Add(CreateTargetFileContent(CreateDocxBytes(new[]
            {
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "P1", "S1", "", "" }
            }), "execution-history-batch-reply-target-b.docx"), "targetFiles", "execution-history-batch-reply-target-b.docx");

            var previewResp = await _client.PostAsync("/api/batch-reply/preview", previewContent);
            previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var executeResp = await _client.PostAsync(
            "/api/batch-reply/execute",
            ApiClientJson.ToJsonContent(new { sessionId }));
        executeResp.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await executeResp.Content.ReadAsStringAsync());

        var executeJson = await executeResp.ReadAsAsync<ApiResponse<JsonElement>>();
        executeJson.Code.Should().Be(0);
        var taskId = executeJson.Data.GetProperty("taskId").GetString();
        taskId.Should().NotBeNullOrWhiteSpace();

        var listResp = await _client.GetAsync("/api/execution-history?page=1&pageSize=20");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResp.ReadAsAsync<ApiResponse<JsonElement>>();
        listJson.Code.Should().Be(0);

        var record = listJson.Data.GetProperty("items").EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("taskId").GetString() == taskId);
        record.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        record.GetProperty("taskType").GetString().Should().Be("batch-reply");
        record.GetProperty("fileCount").GetInt32().Should().Be(2);
        record.GetProperty("adoptedRowCount").GetInt32().Should().Be(2);

        var detailResp = await _client.GetAsync($"/api/execution-history/{record.GetProperty("id").GetInt32()}");
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailJson = await detailResp.ReadAsAsync<ApiResponse<JsonElement>>();
        detailJson.Code.Should().Be(0);

        var batchReplyDetail = detailJson.Data.GetProperty("batchReplyDetail");
        var files = batchReplyDetail.GetProperty("files");
        files.GetArrayLength().Should().Be(2);
        files[0].GetProperty("sheets")[0].GetProperty("rows")[0].GetProperty("status").GetString().Should().Be("adopted");
        files[0].GetProperty("sheets")[0].GetProperty("rows")[0].GetProperty("confidencePercent").GetDouble().Should().Be(100);
    }

    [Fact]
    public async Task BatchReplyExecute_WithLargeArchive_ShouldCompactRowDetailButKeepCounts()
    {
        const int rowCount = 80;
        var longAcceptance = new string('验', 2000);
        var longRemark = new string('备', 2000);

        var sourceRows = new List<string[]> { new[] { "项目", "规格", "验收", "备注" } };
        for (var i = 1; i <= rowCount; i++)
        {
            sourceRows.Add(new[] { $"P{i}", $"S{i}", longAcceptance, longRemark });
        }

        var sessionId = await UploadBatchReplySourceAsync(
            CreateDocxBytes(sourceRows.ToArray()),
            "large-batch-reply-source.docx");

        var targetRows = new List<string[]> { new[] { "项目", "规格", "验收", "备注" } };
        for (var i = 1; i <= rowCount; i++)
        {
            targetRows.Add(new[] { $"P{i}", $"S{i}", "", "" });
        }

        using (var previewContent = new MultipartFormDataContent
        {
            { new StringContent(sessionId), "sessionId" },
            { new StringContent("""[{"tableIndex":0,"projectColumnIndex":0,"specificationColumnIndex":1,"acceptanceColumnIndex":2,"remarkColumnIndex":3,"filterEmptySourceRows":true}]"""), "tableConfigsJson" }
        })
        {
            previewContent.Add(
                CreateTargetFileContent(CreateDocxBytes(targetRows.ToArray()), "large-batch-reply-target.docx"),
                "targetFiles",
                "large-batch-reply-target.docx");

            var previewResp = await _client.PostAsync("/api/batch-reply/preview", previewContent);
            previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var executeResp = await _client.PostAsync(
            "/api/batch-reply/execute",
            ApiClientJson.ToJsonContent(new { sessionId }));
        var executeBody = await executeResp.Content.ReadAsStringAsync();
        executeResp.StatusCode.Should().Be(HttpStatusCode.OK, executeBody);

        var executeJson = await executeResp.ReadAsAsync<ApiResponse<JsonElement>>();
        executeJson.Code.Should().Be(0);
        var taskId = executeJson.Data.GetProperty("taskId").GetString();
        taskId.Should().NotBeNullOrWhiteSpace();

        var listResp = await _client.GetAsync("/api/execution-history?page=1&pageSize=20");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var record = listJson.Data.GetProperty("items").EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("taskId").GetString() == taskId);
        record.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        record.GetProperty("taskType").GetString().Should().Be("batch-reply");
        // 记录级计数从实体列读出，不受逐行明细精简影响
        record.GetProperty("adoptedRowCount").GetInt32().Should().Be(rowCount);

        var detailResp = await _client.GetAsync($"/api/execution-history/{record.GetProperty("id").GetInt32()}");
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailJson = await detailResp.ReadAsAsync<ApiResponse<JsonElement>>();
        detailJson.Code.Should().Be(0);

        // 过大批量回复：保留文件头，但逐行明细被精简掉（避免撑爆持久化与历史查询）
        var compactedFiles = detailJson.Data.GetProperty("batchReplyDetail").GetProperty("files");
        compactedFiles.GetArrayLength().Should().Be(1);
        compactedFiles[0].GetProperty("sheets")[0].GetProperty("rows").GetArrayLength()
            .Should().Be(0, "过大批量回复执行记录应精简掉逐行明细");
    }

    [Fact]
    public async Task BatchReplyExecute_WhenTargetRowsReordered_ShouldPersistExecutionHistoryInTargetRowOrder()
    {
        var sessionId = await UploadBatchReplySourceAsync(
            CreateDocxBytes(new[]
            {
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "P1", "S1", "AC-1", "RM-1" },
                new[] { "P2", "S2", "AC-2", "RM-2" }
            }),
            "execution-history-batch-reply-reordered-source.docx");

        using (var previewContent = new MultipartFormDataContent
        {
            { new StringContent(sessionId), "sessionId" },
            { new StringContent("""[{"tableIndex":0,"projectColumnIndex":0,"specificationColumnIndex":1,"acceptanceColumnIndex":2,"remarkColumnIndex":3,"filterEmptySourceRows":true}]"""), "tableConfigsJson" }
        })
        {
            previewContent.Add(CreateTargetFileContent(CreateDocxBytes(new[]
            {
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "P2", "S2", "", "旧备注2" },
                new[] { "P1", "S1", "", "旧备注1" }
            }), "execution-history-batch-reply-reordered-target.docx"), "targetFiles", "execution-history-batch-reply-reordered-target.docx");

            var previewResp = await _client.PostAsync("/api/batch-reply/preview", previewContent);
            previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var executeResp = await _client.PostAsync(
            "/api/batch-reply/execute",
            ApiClientJson.ToJsonContent(new { sessionId }));
        executeResp.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await executeResp.Content.ReadAsStringAsync());

        var executeJson = await executeResp.ReadAsAsync<ApiResponse<JsonElement>>();
        executeJson.Code.Should().Be(0);
        var taskId = executeJson.Data.GetProperty("taskId").GetString();
        taskId.Should().NotBeNullOrWhiteSpace();

        var listResp = await _client.GetAsync("/api/execution-history?page=1&pageSize=20");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResp.ReadAsAsync<ApiResponse<JsonElement>>();
        listJson.Code.Should().Be(0);

        var record = listJson.Data.GetProperty("items").EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("taskId").GetString() == taskId);
        record.ValueKind.Should().NotBe(JsonValueKind.Undefined);

        var detailResp = await _client.GetAsync($"/api/execution-history/{record.GetProperty("id").GetInt32()}");
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailJson = await detailResp.ReadAsAsync<ApiResponse<JsonElement>>();
        detailJson.Code.Should().Be(0);

        var rows = detailJson.Data.GetProperty("files")[0].GetProperty("sheets")[0].GetProperty("rows");
        rows.GetArrayLength().Should().Be(2);

        rows[0].GetProperty("rowIndex").GetInt32().Should().Be(1);
        rows[0].GetProperty("project").GetString().Should().Be("P2");
        rows[0].GetProperty("specification").GetString().Should().Be("S2");
        rows[0].GetProperty("acceptance").GetString().Should().Be("AC-2");
        rows[0].GetProperty("remark").GetString().Should().Be("RM-2");

        rows[1].GetProperty("rowIndex").GetInt32().Should().Be(2);
        rows[1].GetProperty("project").GetString().Should().Be("P1");
        rows[1].GetProperty("specification").GetString().Should().Be("S1");
        rows[1].GetProperty("acceptance").GetString().Should().Be("AC-1");
        rows[1].GetProperty("remark").GetString().Should().Be("RM-1");
    }

    [Fact]
    public async Task LegacySmartFillExecutionHistory_ShouldReturnDegradedPlaybackInsteadOfRebuild()
    {
        var recordId = await InsertLegacySmartFillExecutionHistoryAsync();

        var detailResp = await _client.GetAsync($"/api/execution-history/{recordId}");
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailJson = await detailResp.ReadAsAsync<ApiResponse<JsonElement>>();
        detailJson.Code.Should().Be(0);

        var playback = detailJson.Data.GetProperty("smartFillPlayback");
        playback.GetProperty("isLegacy").GetBoolean().Should().BeTrue();
        playback.GetProperty("legacyMessage").GetString().Should().Contain("历史记录");

        var legacyFiles = detailJson.Data.GetProperty("files");
        legacyFiles.GetArrayLength().Should().Be(1);
        legacyFiles[0].GetProperty("sheets")[0].GetProperty("rows")[0].GetProperty("status").GetString().Should().Be("adopted");
    }

    [Fact]
    public async Task LegacySmartFillExecutionHistory_WithFullArchive_ShouldReturnArchivedPlaybackRows()
    {
        var recordId = await InsertLegacySmartFillExecutionHistoryWithFullArchiveAsync();

        var detailResp = await _client.GetAsync($"/api/execution-history/{recordId}");
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailJson = await detailResp.ReadAsAsync<ApiResponse<JsonElement>>();
        detailJson.Code.Should().Be(0);

        var playback = detailJson.Data.GetProperty("smartFillPlayback");
        playback.GetProperty("isLegacy").GetBoolean().Should().BeFalse("已有完整归档时应恢复为可展示回放");

        var rows = playback.GetProperty("files")[0].GetProperty("sheets")[0].GetProperty("rows");
        rows.GetArrayLength().Should().Be(2);
        rows[0].GetProperty("rowIndex").GetInt32().Should().Be(1);
        rows[0].GetProperty("status").GetString().Should().Be("adopted");
        rows[0].GetProperty("previewSnapshot").GetProperty("bestMatch").ValueKind
            .Should().Be(JsonValueKind.Null, "轻量详情只保留逐行回放概要");
        rows[0].GetProperty("executionSnapshot").GetProperty("finalAcceptance").ValueKind
            .Should().Be(JsonValueKind.Null, "轻量详情不应携带完整执行文本");
        rows[1].GetProperty("rowIndex").GetInt32().Should().Be(2);
        rows[1].GetProperty("status").GetString().Should().Be("unmatched");

        var rowResp = await _client.GetAsync(
            $"/api/execution-history/{recordId}/smart-fill/rows?fileIndex=0&sheetIndex=0&rowIndex=1");
        rowResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var rowJson = await rowResp.ReadAsAsync<ApiResponse<JsonElement>>();
        rowJson.Code.Should().Be(0);

        var fullRow = rowJson.Data;
        fullRow.GetProperty("rowIndex").GetInt32().Should().Be(1);
        var fullBestMatch = fullRow.GetProperty("previewSnapshot").GetProperty("bestMatch");
        fullBestMatch.GetProperty("project").GetString().Should().Be("P1");
        fullBestMatch.GetProperty("scoreDetails").GetProperty("embedding").GetDouble().Should().Be(0.83);
        fullBestMatch.GetProperty("evidenceSummary").EnumerateArray()
            .Select(item => item.GetString()).Should().Equal("项目一致", "规格语义相近");
        fullBestMatch.GetProperty("topCandidates").GetArrayLength().Should().Be(1);
        fullBestMatch.GetProperty("llmEquivalence").GetProperty("verdict").GetString().Should().Be("equivalent");
        fullBestMatch.GetProperty("llmEquivalence").GetProperty("reason").GetString().Should().Be("语义等价");

        var executionSnapshot = fullRow.GetProperty("executionSnapshot");
        executionSnapshot.GetProperty("overrideAcceptance").GetString().Should().Be("AC-1-人工");
        executionSnapshot.GetProperty("overrideRemark").GetString().Should().Be("RM-1-人工");
        executionSnapshot.GetProperty("finalAcceptance").GetString().Should().Be("AC-1-人工");
        executionSnapshot.GetProperty("finalRemark").GetString().Should().Be("RM-1-人工");
        executionSnapshot.GetProperty("manualConfirmed").GetBoolean().Should().BeTrue();
        executionSnapshot.GetProperty("manualEdited").GetBoolean().Should().BeTrue();
    }

    private async Task<int> UploadDocumentAsync(byte[] bytes, string fileName)
    {
        var businessOrgUnitId = await ResolveBusinessOrgUnitIdAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        content.Add(fileContent, "file", fileName);
        content.Add(new StringContent(businessOrgUnitId.ToString()), "businessOrgUnitId");

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private async Task<string> UploadBatchReplySourceAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        content.Add(CreateTargetFileContent(bytes, fileName), "file", fileName);

        var response = await _client.PostAsync("/api/batch-reply/source/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        return json.Data.GetProperty("sessionId").GetString()!;
    }

    private static ByteArrayContent CreateTargetFileContent(byte[] bytes, string fileName)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(
            fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        return content;
    }

    private async Task<int> CreateCustomerAsync(string name)
    {
        var response = await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("id").GetInt32();
    }

    private async Task<int> CreateProcessAsync(string name)
    {
        var response = await _client.PostAsync("/api/processes", ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("id").GetInt32();
    }

    private async Task<int> CreateSpecAsync(int customerId, int processId, string project, string specification, string acceptance, string remark)
    {
        var businessOrgUnitId = await ResolveBusinessOrgUnitIdAsync();
        var response = await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            businessOrgUnitId,
            customerId,
            processId,
            project,
            specification,
            acceptance,
            remark
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("id").GetInt32();
    }

    private async Task<int> ResolveBusinessOrgUnitIdAsync()
    {
        if (_businessOrgUnitId.HasValue)
            return _businessOrgUnitId.Value;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _businessOrgUnitId = await db.OrgUnits
            .OrderBy(org => org.UnitType == OrgUnitType.Company ? 1 : 0)
            .Select(org => org.Id)
            .FirstAsync();
        return _businessOrgUnitId.Value;
    }

    private async Task<int> InsertLegacySmartFillExecutionHistoryAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var createdAt = DateTime.UtcNow;

        var entity = new ExecutionHistoryRecord
        {
            TaskId = Guid.NewGuid().ToString("N"),
            TaskType = "smart-fill",
            SourceFileId = null,
            SourceFileName = "legacy-smart-fill.docx",
            SourceFileType = UploadedFileType.WordDocx,
            FileCount = 1,
            TotalRowCount = 1,
            MatchedRowCount = 1,
            AdoptedRowCount = 1,
            UnmatchedRowCount = 0,
            SkippedRowCount = 0,
            NotAdoptedRowCount = 0,
            ManualSelectedRowCount = 0,
            CreatedByUserId = 1,
            CompanyId = 1,
            CreatedAt = createdAt,
            DetailJson = """
            {
              "taskId": "legacy-smart-fill",
              "taskType": "smart-fill",
              "sourceFileName": "legacy-smart-fill.docx",
              "fileCount": 1,
              "totalRowCount": 1,
              "matchedRowCount": 1,
              "adoptedRowCount": 1,
              "files": [
                {
                  "fileName": "legacy-smart-fill.docx",
                  "sheets": [
                    {
                      "sheetIndex": 0,
                      "sheetName": "表格 1",
                      "rows": [
                        {
                          "rowIndex": 1,
                          "project": "LP1",
                          "specification": "LS1",
                          "matchedSpecId": 99,
                          "matchedProject": "LP1",
                          "matchedSpecification": "LS1",
                          "acceptance": "LAC-1",
                          "remark": "LRM-1",
                          "confidencePercent": 100,
                          "status": "adopted",
                          "isManualSelected": false,
                          "acceptanceColumnIndex": 2,
                          "remarkColumnIndex": 3
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """
        };

        db.ExecutionHistoryRecords.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    private async Task<int> InsertLegacySmartFillExecutionHistoryWithFullArchiveAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var taskId = Guid.NewGuid().ToString("N");
        var fullDetailJson = JsonSerializer.Serialize(new
        {
            taskId,
            taskType = "smart-fill",
            sourceFileName = "legacy-archive-smart-fill.xlsx",
            fileCount = 1,
            totalRowCount = 2,
            matchedRowCount = 1,
            adoptedRowCount = 1,
            unmatchedRowCount = 1,
            skippedRowCount = 0,
            notAdoptedRowCount = 1,
            manualSelectedRowCount = 0,
            smartFillPlayback = new
            {
                payloadVersion = 1,
                isLegacy = false,
                files = new[]
                {
                    new
                    {
                        fileName = "legacy-archive-smart-fill.xlsx",
                        sheets = new[]
                        {
                            new
                            {
                                sheetIndex = 0,
                                sheetName = "Sheet1",
                                rows = new object[]
                                {
                                    new
                                    {
                                        rowIndex = 1,
                                        sourceProject = "P1",
                                        sourceSpecification = "S1",
                                        status = "adopted",
                                        matchOrigin = "exact",
                                        isManualConfirmed = true,
                                        isManualEdited = true,
                                        displayTags = new[] { "完全匹配", "人工确认", "人工写入" },
                                        previewSnapshot = new
                                        {
                                            confidenceLevel = "high",
                                            bestMatch = new
                                            {
                                                specId = 1,
                                                project = "P1",
                                                specification = "S1",
                                                acceptance = "AC-1",
                                                remark = "RM-1",
                                                score = 0.91,
                                                embeddingScore = 0.83,
                                                scoreDetails = new { embedding = 0.83, rerank = 0.91 },
                                                decision = "manualReview",
                                                selectionMode = "aiRerank",
                                                selectionSummary = "AI 复核后建议人工确认",
                                                matchBasis = "specification",
                                                evidenceSummary = new[] { "项目一致", "规格语义相近" },
                                                conflictSummary = new[] { "规格文本不同" },
                                                issues = Array.Empty<object>(),
                                                entities = Array.Empty<object>(),
                                                topCandidates = new[]
                                                {
                                                    new
                                                    {
                                                        rank = 1,
                                                        specId = 1,
                                                        project = "P1",
                                                        specification = "S1",
                                                        acceptance = "AC-1",
                                                        remark = "RM-1",
                                                        score = 0.91,
                                                        embeddingScore = 0.83,
                                                        scoreDetails = new { embedding = 0.83, rerank = 0.91 },
                                                        decision = "manualReview",
                                                        selectionMode = "aiRerank",
                                                        selectionSummary = "AI 复核保留此候选",
                                                        matchBasis = "specification",
                                                        evidenceSummary = new[] { "项目一致" },
                                                        conflictSummary = new[] { "规格文本不同" },
                                                        issues = Array.Empty<object>(),
                                                        entities = Array.Empty<object>(),
                                                        llmEquivalence = new
                                                        {
                                                            verdict = "equivalent",
                                                            reasonType = "equivalent_expression",
                                                            reason = "语义等价",
                                                            confidence = 0.91
                                                        }
                                                    }
                                                },
                                                recalledCandidateCount = 2,
                                                isAmbiguous = true,
                                                scoreGap = 0.08,
                                                rerankSummary = "AI 认为该候选最接近",
                                                llmEquivalence = new
                                                {
                                                    verdict = "equivalent",
                                                    reasonType = "equivalent_expression",
                                                    reason = "语义等价",
                                                    confidence = 0.91
                                                },
                                                reviewScore = 91.0,
                                                reviewReason = "复核判定语义等价",
                                                reviewCommentary = "允许人工确认采用"
                                            }
                                        },
                                        executionSnapshot = new
                                        {
                                            selectedSpecId = 1,
                                            selectedProject = "P1",
                                            selectedSpecification = "S1",
                                            finalAcceptance = "AC-1-人工",
                                            finalRemark = "RM-1-人工",
                                            overrideAcceptance = "AC-1-人工",
                                            overrideRemark = "RM-1-人工",
                                            manualConfirmed = true,
                                            manualEdited = true,
                                            status = "adopted"
                                        }
                                    },
                                    new
                                    {
                                        rowIndex = 2,
                                        sourceProject = "P2",
                                        sourceSpecification = "S2",
                                        status = "unmatched",
                                        matchOrigin = "none",
                                        isManualConfirmed = false,
                                        isManualEdited = false,
                                        displayTags = new[] { "未采用/未匹配" },
                                        previewSnapshot = new { confidenceLevel = "none", noMatchReason = "未匹配" },
                                        executionSnapshot = new { manualConfirmed = false, manualEdited = false, status = "unmatched" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });
        var archivePath = await storage.SaveSmartFillPlaybackArchiveAsync(
            $"{taskId}-smart-fill-playback.json.gz",
            CompressUtf8(fullDetailJson));

        var entity = new ExecutionHistoryRecord
        {
            TaskId = taskId,
            TaskType = "smart-fill",
            SourceFileId = null,
            SourceFileName = "legacy-archive-smart-fill.xlsx",
            SourceFileType = UploadedFileType.ExcelXlsx,
            FileCount = 1,
            TotalRowCount = 2,
            MatchedRowCount = 1,
            AdoptedRowCount = 1,
            UnmatchedRowCount = 1,
            SkippedRowCount = 0,
            NotAdoptedRowCount = 1,
            ManualSelectedRowCount = 0,
            CreatedByUserId = 1,
            CompanyId = 1,
            CreatedAt = DateTime.UtcNow,
            DetailJson = JsonSerializer.Serialize(new
            {
                taskId,
                taskType = "smart-fill",
                sourceFileName = "legacy-archive-smart-fill.xlsx",
                fileCount = 1,
                totalRowCount = 2,
                matchedRowCount = 1,
                adoptedRowCount = 1,
                unmatchedRowCount = 1,
                notAdoptedRowCount = 1,
                smartFillPlayback = new
                {
                    payloadVersion = 1,
                    isLegacy = true,
                    hasFullArchive = true,
                    fullArchiveRelativePath = archivePath,
                    legacyMessage = "执行记录过大，已自动压缩，仅保留汇总信息。",
                    files = Array.Empty<object>()
                }
            })
        };

        db.ExecutionHistoryRecords.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    private static byte[] CompressUtf8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    private static byte[] CreateDocxBytes(params string[][] rows)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;
            var table = new Table();

            foreach (var rowValues in rows)
            {
                var row = new TableRow();
                foreach (var value in rowValues)
                {
                    row.Append(new TableCell(new Paragraph(new Run(new Text(value ?? string.Empty)))));
                }
                table.Append(row);
            }

            body.Append(table);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static byte[] CreateExcelBytes(params string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            {
                sheet.Cell(rowIndex + 1, columnIndex + 1).Value = rows[rowIndex][columnIndex];
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
