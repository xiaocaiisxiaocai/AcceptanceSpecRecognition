using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

/// <summary>
/// 批量回复 API 集成测试
/// </summary>
public class BatchReplyApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BatchReplyApiTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SourceUpload_And_GetTables_ForDocx_ShouldReturnSessionAndTables()
    {
        var sourceDocxBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "AC-1", "RM-1" }
        });

        using var uploadContent = new MultipartFormDataContent();
        var sourceContent = new ByteArrayContent(sourceDocxBytes);
        sourceContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        uploadContent.Add(sourceContent, "file", "batch-reply-source.docx");

        var uploadResp = await _client.PostAsync("/api/batch-reply/source/upload", uploadContent);

        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        uploadJson.Code.Should().Be(0);

        var sessionId = uploadJson.Data.GetProperty("sessionId").GetString();
        sessionId.Should().NotBeNullOrWhiteSpace();

        var tablesResp = await _client.GetAsync($"/api/batch-reply/sessions/{sessionId}/tables");
        tablesResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var tablesJson = await tablesResp.ReadAsAsync<ApiResponse<JsonElement>>();
        tablesJson.Code.Should().Be(0);
        tablesJson.Data.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Preview_WithSameDocxTarget_ShouldReturnReadyFile()
    {
        var sourceSessionId = await UploadSourceAsync(
            CreateDocxBytes(new[]
            {
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "P1", "S1", "AC-1", "RM-1" },
                new[] { "P2", "S2", "AC-2", "" }
            }),
            "batch-reply-preview-source.docx");

        using var previewContent = new MultipartFormDataContent
        {
            { new StringContent(sourceSessionId), "sessionId" },
            { new StringContent("""[{"tableIndex":0,"projectColumnIndex":0,"specificationColumnIndex":1,"acceptanceColumnIndex":2,"remarkColumnIndex":3,"filterEmptySourceRows":true}]"""), "tableConfigsJson" }
        };

        var targetBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "原备注" },
            new[] { "P2", "S2", "", "原备注2" }
        });
        previewContent.Add(new ByteArrayContent(targetBytes), "targetFiles", "batch-reply-target.docx");

        var previewResp = await _client.PostAsync("/api/batch-reply/preview", previewContent);

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);
        previewJson.Data.GetProperty("readyCount").GetInt32().Should().Be(1);
        previewJson.Data.GetProperty("files")[0].GetProperty("canApply").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithSameDocxTarget_ShouldWriteAcceptanceAndRemark()
    {
        var sourceSessionId = await UploadSourceAsync(
            CreateDocxBytes(new[]
            {
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "P1", "S1", "AC-1", "RM-1" },
                new[] { "P2", "S2", "AC-2", "" }
            }),
            "batch-reply-execute-source.docx");

        using (var previewContent = new MultipartFormDataContent
        {
            { new StringContent(sourceSessionId), "sessionId" },
            { new StringContent("""[{"tableIndex":0,"projectColumnIndex":0,"specificationColumnIndex":1,"acceptanceColumnIndex":2,"remarkColumnIndex":3,"filterEmptySourceRows":true}]"""), "tableConfigsJson" }
        })
        {
            var targetBytes = CreateDocxBytes(new[]
            {
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "P1", "S1", "", "旧备注1" },
                new[] { "P2", "S2", "", "旧备注2" }
            });
            previewContent.Add(new ByteArrayContent(targetBytes), "targetFiles", "batch-reply-target-execute.docx");

            var previewResp = await _client.PostAsync("/api/batch-reply/preview", previewContent);
            previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var executeResp = await _client.PostAsync(
            "/api/batch-reply/execute",
            ApiClientJson.ToJsonContent(new
            {
                sessionId = sourceSessionId
            }));

        executeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var executeJson = await executeResp.ReadAsAsync<ApiResponse<JsonElement>>();
        executeJson.Code.Should().Be(0);
        executeJson.Data.GetProperty("successCount").GetInt32().Should().Be(1);

        var taskId = executeJson.Data.GetProperty("taskId").GetString();
        taskId.Should().NotBeNullOrWhiteSpace();

        var downloadResp = await _client.GetAsync($"/api/batch-reply/download/{taskId}");
        downloadResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var resultBytes = await downloadResp.Content.ReadAsByteArrayAsync();
        GetDocxCellText(resultBytes, 0, 1, 2).Should().Be("AC-1");
        GetDocxCellText(resultBytes, 0, 1, 3).Should().Be("RM-1");
        GetDocxCellText(resultBytes, 0, 2, 2).Should().Be("AC-2");
        GetDocxCellText(resultBytes, 0, 2, 3).Should().Be(string.Empty);
    }

    [Fact]
    public async Task Execute_WhenTargetRowsReorderedButProjectAndSpecificationMatch_ShouldStillWriteBack()
    {
        var sourceSessionId = await UploadSourceAsync(
            CreateDocxBytes(new[]
            {
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "P1", "S1", "AC-1", "RM-1" },
                new[] { "P2", "S2", "AC-2", "RM-2" }
            }),
            "batch-reply-reordered-source.docx");

        using (var previewContent = new MultipartFormDataContent
        {
            { new StringContent(sourceSessionId), "sessionId" },
            { new StringContent("""[{"tableIndex":0,"projectColumnIndex":0,"specificationColumnIndex":1,"acceptanceColumnIndex":2,"remarkColumnIndex":3,"filterEmptySourceRows":true}]"""), "tableConfigsJson" }
        })
        {
            var targetBytes = CreateDocxBytes(new[]
            {
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "P2", "S2", "", "旧备注2" },
                new[] { "P1", "S1", "", "旧备注1" }
            });
            previewContent.Add(new ByteArrayContent(targetBytes), "targetFiles", "batch-reply-reordered-target.docx");

            var previewResp = await _client.PostAsync("/api/batch-reply/preview", previewContent);
            previewResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
            previewJson.Code.Should().Be(0);
            previewJson.Data.GetProperty("readyCount").GetInt32().Should().Be(1);
            previewJson.Data.GetProperty("files")[0].GetProperty("canApply").GetBoolean().Should().BeTrue();
        }

        var executeResp = await _client.PostAsync(
            "/api/batch-reply/execute",
            ApiClientJson.ToJsonContent(new
            {
                sessionId = sourceSessionId
            }));

        executeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var executeJson = await executeResp.ReadAsAsync<ApiResponse<JsonElement>>();
        executeJson.Code.Should().Be(0);
        executeJson.Data.GetProperty("successCount").GetInt32().Should().Be(1);

        var taskId = executeJson.Data.GetProperty("taskId").GetString();
        taskId.Should().NotBeNullOrWhiteSpace();

        var downloadResp = await _client.GetAsync($"/api/batch-reply/download/{taskId}");
        downloadResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var resultBytes = await downloadResp.Content.ReadAsByteArrayAsync();
        GetDocxCellText(resultBytes, 0, 1, 2).Should().Be("AC-2");
        GetDocxCellText(resultBytes, 0, 1, 3).Should().Be("RM-2");
        GetDocxCellText(resultBytes, 0, 2, 2).Should().Be("AC-1");
        GetDocxCellText(resultBytes, 0, 2, 3).Should().Be("RM-1");
    }

    [Fact]
    public async Task Preview_WhenSourceContainsDuplicateProjectAndSpecification_ShouldReject()
    {
        var sourceSessionId = await UploadSourceAsync(
            CreateDocxBytes(new[]
            {
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "P1", "S1", "AC-1", "RM-1" },
                new[] { "P1", "S1", "AC-2", "RM-2" }
            }),
            "batch-reply-duplicate-source.docx");

        using var previewContent = new MultipartFormDataContent
        {
            { new StringContent(sourceSessionId), "sessionId" },
            { new StringContent("""[{"tableIndex":0,"projectColumnIndex":0,"specificationColumnIndex":1,"acceptanceColumnIndex":2,"remarkColumnIndex":3,"filterEmptySourceRows":true}]"""), "tableConfigsJson" }
        };

        var targetBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "旧备注1" },
            new[] { "P2", "S2", "", "旧备注2" }
        });
        previewContent.Add(new ByteArrayContent(targetBytes), "targetFiles", "batch-reply-duplicate-target.docx");

        var previewResp = await _client.PostAsync("/api/batch-reply/preview", previewContent);

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);
        previewJson.Data.GetProperty("readyCount").GetInt32().Should().Be(0);
        previewJson.Data.GetProperty("files")[0].GetProperty("canApply").GetBoolean().Should().BeFalse();
        previewJson.Data.GetProperty("files")[0].GetProperty("errors")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain("表格1存在重复的项目/规格组合，请手动处理");
    }

    [Fact]
    public async Task Preview_WhenTargetContainsDuplicateProjectAndSpecification_ShouldReject()
    {
        var sourceSessionId = await UploadSourceAsync(
            CreateDocxBytes(new[]
            {
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "P1", "S1", "AC-1", "RM-1" },
                new[] { "P2", "S2", "AC-2", "RM-2" }
            }),
            "batch-reply-duplicate-target-source.docx");

        using var previewContent = new MultipartFormDataContent
        {
            { new StringContent(sourceSessionId), "sessionId" },
            { new StringContent("""[{"tableIndex":0,"projectColumnIndex":0,"specificationColumnIndex":1,"acceptanceColumnIndex":2,"remarkColumnIndex":3,"filterEmptySourceRows":true}]"""), "tableConfigsJson" }
        };

        var targetBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "旧备注1" },
            new[] { "P1", "S1", "", "旧备注2" }
        });
        previewContent.Add(new ByteArrayContent(targetBytes), "targetFiles", "batch-reply-duplicate-target.docx");

        var previewResp = await _client.PostAsync("/api/batch-reply/preview", previewContent);

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);
        previewJson.Data.GetProperty("readyCount").GetInt32().Should().Be(0);
        previewJson.Data.GetProperty("files")[0].GetProperty("canApply").GetBoolean().Should().BeFalse();
        previewJson.Data.GetProperty("files")[0].GetProperty("errors")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain("表格1存在重复的项目/规格组合，请手动处理");
    }

    [Fact]
    public async Task Download_WhenArtifactCacheMisses_ShouldFallbackToPersistedArtifact()
    {
        var sourceSessionId = await UploadSourceAsync(
            CreateDocxBytes(new[]
            {
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "P1", "S1", "AC-1", "RM-1" }
            }),
            "batch-reply-persisted-download-source.docx");

        using (var previewContent = new MultipartFormDataContent
        {
            { new StringContent(sourceSessionId), "sessionId" },
            { new StringContent("""[{"tableIndex":0,"projectColumnIndex":0,"specificationColumnIndex":1,"acceptanceColumnIndex":2,"remarkColumnIndex":3,"filterEmptySourceRows":true}]"""), "tableConfigsJson" }
        })
        {
            var targetBytes = CreateDocxBytes(new[]
            {
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "P1", "S1", "", "旧备注" }
            });
            previewContent.Add(new ByteArrayContent(targetBytes), "targetFiles", "batch-reply-persisted-download-target.docx");

            var previewResp = await _client.PostAsync("/api/batch-reply/preview", previewContent);
            previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var executeResp = await _client.PostAsync(
            "/api/batch-reply/execute",
            ApiClientJson.ToJsonContent(new
            {
                sessionId = sourceSessionId
            }));

        executeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var executeJson = await executeResp.ReadAsAsync<ApiResponse<JsonElement>>();
        executeJson.Code.Should().Be(0);
        var taskId = executeJson.Data.GetProperty("taskId").GetString();
        taskId.Should().NotBeNullOrWhiteSpace();

        using (var scope = _factory.Services.CreateScope())
        {
            var memoryCache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
            memoryCache.Remove($"batch-reply:artifact:{taskId}");
        }

        var downloadResp = await _client.GetAsync($"/api/batch-reply/download/{taskId}");
        downloadResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var resultBytes = await downloadResp.Content.ReadAsByteArrayAsync();
        GetDocxCellText(resultBytes, 0, 1, 2).Should().Be("AC-1");
        GetDocxCellText(resultBytes, 0, 1, 3).Should().Be("RM-1");
    }

    [Fact]
    public async Task Execute_WhenSessionCacheMisses_ShouldFallbackToPersistedSessionManifest()
    {
        var sourceSessionId = await UploadSourceAsync(
            CreateDocxBytes(new[]
            {
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "P1", "S1", "AC-1", "RM-1" }
            }),
            "batch-reply-session-persist-source.docx");

        using (var previewContent = new MultipartFormDataContent
        {
            { new StringContent(sourceSessionId), "sessionId" },
            { new StringContent("""[{"tableIndex":0,"projectColumnIndex":0,"specificationColumnIndex":1,"acceptanceColumnIndex":2,"remarkColumnIndex":3,"filterEmptySourceRows":true}]"""), "tableConfigsJson" }
        })
        {
            var targetBytes = CreateDocxBytes(new[]
            {
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "P1", "S1", "", "旧备注" }
            });
            previewContent.Add(new ByteArrayContent(targetBytes), "targetFiles", "batch-reply-session-persist-target.docx");

            var previewResp = await _client.PostAsync("/api/batch-reply/preview", previewContent);
            previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var memoryCache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
            memoryCache.Remove($"batch-reply:session:{sourceSessionId}");
        }

        var executeResp = await _client.PostAsync(
            "/api/batch-reply/execute",
            ApiClientJson.ToJsonContent(new
            {
                sessionId = sourceSessionId
            }));

        executeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var executeJson = await executeResp.ReadAsAsync<ApiResponse<JsonElement>>();
        executeJson.Code.Should().Be(0);
        executeJson.Data.GetProperty("successCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Preview_WhenSourceAndTargetFormatsDiffer_ShouldReject()
    {
        var sourceSessionId = await UploadSourceAsync(
            CreateDocxBytes(new[]
            {
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "P1", "S1", "AC-1", "RM-1" }
            }),
            "batch-reply-source-mismatch.docx");

        using var previewContent = new MultipartFormDataContent
        {
            { new StringContent(sourceSessionId), "sessionId" },
            { new StringContent("""[{"tableIndex":0,"projectColumnIndex":0,"specificationColumnIndex":1,"acceptanceColumnIndex":2,"remarkColumnIndex":3,"filterEmptySourceRows":true}]"""), "tableConfigsJson" }
        };

        var xlsxBytes = CreateExcelBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" }
        });
        var targetContent = new ByteArrayContent(xlsxBytes);
        targetContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        previewContent.Add(targetContent, "targetFiles", "batch-reply-mismatch.xlsx");

        var previewResp = await _client.PostAsync("/api/batch-reply/preview", previewContent);

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);
        previewJson.Data.GetProperty("readyCount").GetInt32().Should().Be(0);
        previewJson.Data.GetProperty("files")[0].GetProperty("canApply").GetBoolean().Should().BeFalse();
        previewJson.Data.GetProperty("files")[0].GetProperty("errors")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain("文件类型不一致");
    }

    [Fact]
    public async Task Preview_WithExcelAndCustomRows_ShouldReturnReadyFile()
    {
        var sourceSessionId = await UploadSourceAsync(
            CreateExcelBytes(new[]
            {
                new[] { "", "", "", "" },
                new[] { "项目", "规格", "验收", "备注" },
                new[] { "说明", "说明", "说明", "说明" },
                new[] { "P1", "S1", "AC-1", "RM-1" },
                new[] { "P2", "S2", "AC-2", "" }
            }),
            "batch-reply-source.xlsx");

        using var previewContent = new MultipartFormDataContent
        {
            { new StringContent(sourceSessionId), "sessionId" },
            { new StringContent("""[{"tableIndex":0,"projectColumnIndex":0,"specificationColumnIndex":1,"acceptanceColumnIndex":2,"remarkColumnIndex":3,"headerRowStart":2,"headerRowCount":2,"dataStartRow":4,"filterEmptySourceRows":true}]"""), "tableConfigsJson" }
        };

        var targetXlsx = CreateExcelBytes(new[]
        {
            new[] { "", "", "", "" },
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "说明", "说明", "说明", "说明" },
            new[] { "P1", "S1", "", "旧备注1" },
            new[] { "P2", "S2", "", "旧备注2" }
        });
        var targetContent = new ByteArrayContent(targetXlsx);
        targetContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        previewContent.Add(targetContent, "targetFiles", "batch-reply-target.xlsx");

        var previewResp = await _client.PostAsync("/api/batch-reply/preview", previewContent);

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);
        previewJson.Data.GetProperty("readyCount").GetInt32().Should().Be(1);
        previewJson.Data.GetProperty("files")[0].GetProperty("canApply").GetBoolean().Should().BeTrue();
    }

    private async Task<string> UploadSourceAsync(byte[] bytes, string fileName)
    {
        using var uploadContent = new MultipartFormDataContent();
        var sourceContent = new ByteArrayContent(bytes);
        sourceContent.Headers.ContentType = new MediaTypeHeaderValue(
            fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        uploadContent.Add(sourceContent, "file", fileName);

        var uploadResp = await _client.PostAsync("/api/batch-reply/source/upload", uploadContent);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        uploadJson.Code.Should().Be(0);
        return uploadJson.Data.GetProperty("sessionId").GetString()!;
    }

    private static byte[] CreateDocxBytes(params string[][][] tables)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;

            foreach (var rows in tables)
            {
                var table = new Table();
                table.AppendChild(new TableProperties(new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4 },
                    new BottomBorder { Val = BorderValues.Single, Size = 4 },
                    new LeftBorder { Val = BorderValues.Single, Size = 4 },
                    new RightBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

                foreach (var row in rows)
                {
                    var tableRow = new TableRow();
                    foreach (var cell in row)
                    {
                        tableRow.AppendChild(new TableCell(new Paragraph(new Run(new Text(cell ?? string.Empty)))
                        {
                            ParagraphProperties = new ParagraphProperties()
                        }));
                    }

                    table.AppendChild(tableRow);
                }

                body.Append(table);
                body.Append(new Paragraph());
            }

            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static byte[] CreateExcelBytes(string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Sheet1");
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            {
                worksheet.Cell(rowIndex + 1, columnIndex + 1).Value = rows[rowIndex][columnIndex] ?? string.Empty;
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string GetDocxCellText(byte[] docxBytes, int tableIndex, int rowIndex, int columnIndex)
    {
        using var stream = new MemoryStream(docxBytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var tables = document.MainDocumentPart!.Document!.Body!.Descendants<Table>().ToList();
        var targetTable = tables[tableIndex];
        var targetRow = targetTable.Elements<TableRow>().ToList()[rowIndex];
        var targetCell = targetRow.Elements<TableCell>().ToList()[columnIndex];
        return targetCell.InnerText ?? string.Empty;
    }
}
