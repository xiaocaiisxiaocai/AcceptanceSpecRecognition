using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

/// <summary>
/// 执行记录 API 集成测试
/// </summary>
public class ExecutionHistoryApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ExecutionHistoryApiTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SmartFillExecute_ShouldPersistExecutionHistory_AndExposeListAndDetail()
    {
        var docxBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" },
            new[] { "P2", "S2", "", "" }
        });

        var fileId = await UploadDocumentAsync(docxBytes, "execution-history-smart-fill.docx");
        var customerId = await CreateCustomerAsync("ExecutionHistory-C1");
        var processId = await CreateProcessAsync("ExecutionHistory-P1");
        var specId = await CreateSpecAsync(customerId, processId, "P1", "S1", "AC-1", "RM-1");

        var executeResp = await _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                highConfidenceThreshold = 0.95,
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
                            new { rowIndex = 1, specId, matchScore = 1.0 }
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
        record.GetProperty("totalRowCount").GetInt32().Should().Be(2);
        record.GetProperty("adoptedRowCount").GetInt32().Should().Be(1);
        record.GetProperty("unmatchedRowCount").GetInt32().Should().Be(1);

        var detailId = record.GetProperty("id").GetInt32();
        var detailResp = await _client.GetAsync($"/api/execution-history/{detailId}");
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailJson = await detailResp.ReadAsAsync<ApiResponse<JsonElement>>();
        detailJson.Code.Should().Be(0);

        var files = detailJson.Data.GetProperty("files");
        files.GetArrayLength().Should().Be(1);

        var rows = files[0].GetProperty("sheets")[0].GetProperty("rows");
        rows.GetArrayLength().Should().Be(2);

        rows[0].GetProperty("status").GetString().Should().Be("adopted");
        rows[0].GetProperty("confidencePercent").GetDouble().Should().Be(100);
        rows[0].GetProperty("isManualSelected").GetBoolean().Should().BeFalse();

        rows[1].GetProperty("status").GetString().Should().Be("unmatched");
        rows[1].GetProperty("confidencePercent").GetDouble().Should().Be(0);
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

        var files = detailJson.Data.GetProperty("files");
        files.GetArrayLength().Should().Be(2);
        files[0].GetProperty("sheets")[0].GetProperty("rows")[0].GetProperty("status").GetString().Should().Be("adopted");
        files[0].GetProperty("sheets")[0].GetProperty("rows")[0].GetProperty("confidencePercent").GetDouble().Should().Be(100);
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
