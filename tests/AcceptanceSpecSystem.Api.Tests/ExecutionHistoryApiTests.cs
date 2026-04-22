using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

/// <summary>
/// 执行记录 API 集成测试
/// </summary>
public class ExecutionHistoryApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ExecutionHistoryApiTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SmartFillExecute_ShouldPersistPlaybackSummary_AndExposeSmartFillPlaybackDetail()
    {
        var docxBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" },
            new[] { "P2", "S2", "", "" },
            new[] { "P3", "S3", "", "" }
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
                                    evidenceSummary = new[] { "项目一致" },
                                    conflictSummary = new[] { "规格表述存在轻微差异" },
                                    issues = Array.Empty<object>(),
                                    entities = Array.Empty<object>(),
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
                            new { rowIndex = 1, specId },
                            new
                            {
                                rowIndex = 2,
                                specId = specId2,
                                manualConfirmed = true,
                                overrideAcceptance = "AC-2-人工",
                                overrideRemark = "RM-2-人工"
                            }
                        }
                    }
                }
            }));

        executeResp.StatusCode.Should().Be(HttpStatusCode.OK);
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
        record.GetProperty("totalRowCount").GetInt32().Should().Be(3);
        record.GetProperty("adoptedRowCount").GetInt32().Should().Be(2);
        record.GetProperty("unmatchedRowCount").GetInt32().Should().Be(1);
        record.GetProperty("smartFillSummary").GetProperty("exactMatchedRowCount").GetInt32().Should().Be(1);
        record.GetProperty("smartFillSummary").GetProperty("aiMatchedRowCount").GetInt32().Should().Be(1);
        record.GetProperty("smartFillSummary").GetProperty("manualConfirmedRowCount").GetInt32().Should().Be(1);
        record.GetProperty("smartFillSummary").GetProperty("manualEditedRowCount").GetInt32().Should().Be(1);
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
        rows.GetArrayLength().Should().Be(3);

        rows[0].GetProperty("displayTags")[0].GetString().Should().Be("完全匹配");
        rows[0].GetProperty("previewSnapshot").GetProperty("bestMatch").GetProperty("selectionMode").GetString().Should().Be("exactShortcut");
        rows[0].GetProperty("previewSnapshot").GetProperty("bestMatch").GetProperty("topCandidates").GetArrayLength().Should().Be(0, "完全一致的归档无需重复保存候选列表");
        rows[0].GetProperty("executionSnapshot").GetProperty("finalAcceptance").GetString().Should().Be("AC-1");

        rows[1].GetProperty("matchOrigin").GetString().Should().Be("ai");
        rows[1].GetProperty("displayTags").EnumerateArray().Select(item => item.GetString()).Should().Equal("AI匹配", "人工确认", "人工写入");
        rows[1].GetProperty("previewSnapshot").GetProperty("bestMatch").GetProperty("selectionMode").GetString().Should().Be("aiRerank");
        rows[1].GetProperty("previewSnapshot").GetProperty("bestMatch").GetProperty("topCandidates").GetArrayLength().Should().Be(1, "非完全一致的归档仍需保留候选上下文");
        rows[1].GetProperty("previewSnapshot").GetProperty("bestMatch").GetProperty("llmEquivalence").GetProperty("verdict").GetString().Should().Be("equivalent");
        rows[1].GetProperty("executionSnapshot").GetProperty("manualConfirmed").GetBoolean().Should().BeTrue();
        rows[1].GetProperty("executionSnapshot").GetProperty("manualEdited").GetBoolean().Should().BeTrue();
        rows[1].GetProperty("executionSnapshot").GetProperty("finalAcceptance").GetString().Should().Be("AC-2-人工");
        rows[1].GetProperty("executionSnapshot").GetProperty("finalRemark").GetString().Should().Be("RM-2-人工");

        rows[2].GetProperty("displayTags").EnumerateArray().Select(item => item.GetString()).Should().Equal("未采用/未匹配");
        rows[2].GetProperty("previewSnapshot").GetProperty("noMatchReason").GetString().Should().Be("未找到可采用候选");
        rows[2].GetProperty("executionSnapshot").GetProperty("status").GetString().Should().Be("unmatched");
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
        executeResp.StatusCode.Should().Be(HttpStatusCode.OK);

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
        executeResp.StatusCode.Should().Be(HttpStatusCode.OK);

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

    private async Task<int> UploadDocumentAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
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
        var response = await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
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
