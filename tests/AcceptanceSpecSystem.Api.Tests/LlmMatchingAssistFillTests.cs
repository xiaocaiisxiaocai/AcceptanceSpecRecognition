using System.Net;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;

namespace AcceptanceSpecSystem.Api.Tests;

/// <summary>
/// LLM 复核辅助填充集成测试
/// </summary>
public class LlmMatchingAssistFillTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public LlmMatchingAssistFillTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    /// <summary>
    /// 已停用 LLM 建议直接写回
    /// </summary>
    [Fact]
    public async Task ExecuteFill_WithLlmSuggestion_ShouldReturnBadRequest()
    {
        var docxBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" }
        });

        var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(docxBytes), "file", "llm-fill.docx");
        var uploadResp = await _client.PostAsync("/api/documents/upload", multipart);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();

        var execResp = await _client.PostAsync("/api/matching/execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                tableIndex = 0,
                acceptanceColumnIndex = 2,
                remarkColumnIndex = 3,
                mappings = new[]
                {
                    new
                    {
                        rowIndex = 1,
                        useLlmSuggestion = true,
                        acceptance = "LLM-AC",
                        remark = "LLM-REM"
                    }
                }
            }));

        execResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(400);
        execJson.Message.Should().Contain("已停用 LLM 生成建议写回");
    }

    [Fact]
    public async Task ExecuteFill_WithMediumScoreWithoutReview_ShouldSkip()
    {
        var (fileId, specId) = await PrepareSingleSpecFillAsync("SkipMedium");

        var execResp = await _client.PostAsync("/api/matching/execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                tableIndex = 0,
                acceptanceColumnIndex = 2,
                remarkColumnIndex = 3,
                highConfidenceThreshold = 0.95,
                mappings = new[]
                {
                    new
                    {
                        rowIndex = 1,
                        specId,
                        matchScore = 0.88
                    }
                }
            }));

        execResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(0);
        execJson.Data.GetProperty("filledCount").GetInt32().Should().Be(0);
        execJson.Data.GetProperty("skippedCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteFill_WithReviewedMediumScore_ShouldFillMatchedSpec()
    {
        var (fileId, specId) = await PrepareSingleSpecFillAsync("FillReviewed");

        var execResp = await _client.PostAsync("/api/matching/execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                tableIndex = 0,
                acceptanceColumnIndex = 2,
                remarkColumnIndex = 3,
                highConfidenceThreshold = 0.95,
                mappings = new[]
                {
                    new
                    {
                        rowIndex = 1,
                        specId,
                        matchScore = 0.88,
                        llmReviewScore = 95
                    }
                }
            }));

        execResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(0);
        execJson.Data.GetProperty("filledCount").GetInt32().Should().Be(1);
        var taskId = execJson.Data.GetProperty("taskId").GetString();

        var dlResp = await _client.GetAsync($"/api/matching/download/{taskId}");
        dlResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var filledBytes = await dlResp.Content.ReadAsByteArrayAsync();

        GetCellText(filledBytes, 0, 1, 2).Should().Be("DB-AC-1");
        GetCellText(filledBytes, 0, 1, 3).Should().Be("DB-REM-1");
    }

    [Fact]
    public async Task ExecuteFill_WithCustomHighConfidenceThreshold_ShouldFillWithoutReview()
    {
        var (fileId, specId) = await PrepareSingleSpecFillAsync("CustomThreshold");

        var execResp = await _client.PostAsync("/api/matching/execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                tableIndex = 0,
                acceptanceColumnIndex = 2,
                remarkColumnIndex = 3,
                highConfidenceThreshold = 0.85,
                mappings = new[]
                {
                    new
                    {
                        rowIndex = 1,
                        specId,
                        matchScore = 0.9
                    }
                }
            }));

        execResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(0);
        execJson.Data.GetProperty("filledCount").GetInt32().Should().Be(1);
        execJson.Data.GetProperty("skippedCount").GetInt32().Should().Be(0);
    }

    #region Helpers

    private async Task<(int FileId, int SpecId)> PrepareSingleSpecFillAsync(string prefix)
    {
        var docxBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" }
        });

        var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(docxBytes), "file", $"{prefix}.docx");
        var uploadResp = await _client.PostAsync("/api/documents/upload", multipart);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();

        var customerId = (await (await _client.PostAsync("/api/customers",
            ApiClientJson.ToJsonContent(new { name = $"{prefix}-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processId = (await (await _client.PostAsync("/api/processes",
            ApiClientJson.ToJsonContent(new { name = $"{prefix}-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var specResp = await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "P1",
            specification = "S1",
            acceptance = "DB-AC-1",
            remark = "DB-REM-1"
        }));
        var specId = (await specResp.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        return (fileId, specId);
    }

    private static byte[] CreateDocxBytes(string[][] rows)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());

            var table = new Table();
            table.AppendChild(new TableProperties(new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }
            )));

            foreach (var row in rows)
            {
                var tr = new TableRow();
                foreach (var cell in row)
                {
                    tr.AppendChild(new TableCell(new Paragraph(new Run(new Text(cell ?? string.Empty)))
                    {
                        ParagraphProperties = new ParagraphProperties()
                    }));
                }
                table.AppendChild(tr);
            }

            main.Document.Body!.Append(table);
            main.Document.Save();
        }

        return ms.ToArray();
    }

    private static string GetCellText(byte[] docx, int tableIndex, int rowIndex, int colIndex)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var tables = body.Descendants<Table>().ToList();
        var table = tables[tableIndex];
        var row = table.Elements<TableRow>().ToList()[rowIndex];
        var cell = row.Elements<TableCell>().ToList()[colIndex];
        return cell.InnerText ?? string.Empty;
    }

    #endregion
}
