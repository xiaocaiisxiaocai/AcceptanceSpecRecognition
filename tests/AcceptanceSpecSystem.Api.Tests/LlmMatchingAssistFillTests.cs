using System.Net;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;

namespace AcceptanceSpecSystem.Api.Tests;

/// <summary>
/// LLM 建议辅助填充路径集成测试（任务 5.3）
/// </summary>
public class LlmMatchingAssistFillTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public LlmMatchingAssistFillTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    /// <summary>
    /// 使用 LLM 建议填充路径：useLlmSuggestion=true 时，自定义 acceptance/remark 应被写入文档
    /// </summary>
    [Fact]
    public async Task ExecuteFill_WithLlmSuggestion_ShouldFillCustomAcceptance()
    {
        // 1) 构造 docx
        var docxBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" }
        });

        // 2) 上传
        var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(docxBytes), "file", "llm-fill.docx");
        var uploadResp = await _client.PostAsync("/api/documents/upload", multipart);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();

        // 3) 创建基础数据（需要至少存在有效的验收规格环境）
        var customerId = (await (await _client.PostAsync("/api/customers",
            ApiClientJson.ToJsonContent(new { name = "LlmFill-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processId = (await (await _client.PostAsync("/api/processes",
            ApiClientJson.ToJsonContent(new { name = "LlmFill-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        // 4) 执行填充（LLM 建议路径，不需要 specId）
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

        execResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(0);
        execJson.Data.GetProperty("filledCount").GetInt32().Should().Be(1);
        var taskId = execJson.Data.GetProperty("taskId").GetString();

        // 5) 下载验证
        var dlResp = await _client.GetAsync($"/api/matching/download/{taskId}");
        dlResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var filledBytes = await dlResp.Content.ReadAsByteArrayAsync();

        GetCellText(filledBytes, 0, 1, 2).Should().Be("LLM-AC");
        GetCellText(filledBytes, 0, 1, 3).Should().Be("LLM-REM");
    }

    /// <summary>
    /// 混合映射路径：一行使用 specId，一行使用 LLM 建议
    /// </summary>
    [Fact]
    public async Task ExecuteFill_WithMixedMappings_ShouldHandleBothPaths()
    {
        // 1) 构造双行 docx
        var docxBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" },
            new[] { "P2", "S2", "", "" }
        });

        // 2) 上传
        var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(docxBytes), "file", "mixed-fill.docx");
        var uploadResp = await _client.PostAsync("/api/documents/upload", multipart);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();

        // 3) 创建规格数据（仅为第一行匹配用）
        var customerId = (await (await _client.PostAsync("/api/customers",
            ApiClientJson.ToJsonContent(new { name = "MixFill-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processId = (await (await _client.PostAsync("/api/processes",
            ApiClientJson.ToJsonContent(new { name = "MixFill-P" })))
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

        // 4) 执行混合填充
        var execResp = await _client.PostAsync("/api/matching/execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                tableIndex = 0,
                acceptanceColumnIndex = 2,
                remarkColumnIndex = 3,
                mappings = new object[]
                {
                    new { rowIndex = 1, specId, useLlmSuggestion = false },
                    new { rowIndex = 2, useLlmSuggestion = true, acceptance = "LLM-AC-2", remark = "LLM-REM-2" }
                }
            }));

        execResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(0);
        execJson.Data.GetProperty("filledCount").GetInt32().Should().Be(2);
        var taskId = execJson.Data.GetProperty("taskId").GetString();

        // 5) 下载并验证两行各自填充正确
        var dlResp = await _client.GetAsync($"/api/matching/download/{taskId}");
        dlResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var filledBytes = await dlResp.Content.ReadAsByteArrayAsync();

        // 行1：来自数据库规格
        GetCellText(filledBytes, 0, 1, 2).Should().Be("DB-AC-1");
        GetCellText(filledBytes, 0, 1, 3).Should().Be("DB-REM-1");

        // 行2：来自 LLM 建议
        GetCellText(filledBytes, 0, 2, 2).Should().Be("LLM-AC-2");
        GetCellText(filledBytes, 0, 2, 3).Should().Be("LLM-REM-2");
    }

    #region Helpers

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
