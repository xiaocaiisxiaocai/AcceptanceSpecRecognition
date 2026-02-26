using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Documents.Writers;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;

namespace AcceptanceSpecSystem.Api.Tests;

/// <summary>
/// 整份文档批量填充（多表格一次性填充）集成测试
/// </summary>
public class BatchFillTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public BatchFillTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    /// <summary>
    /// 批量预览：含 2 个表格的 docx，batch-preview 返回每个表的独立匹配结果
    /// </summary>
    [Fact]
    public async Task BatchPreview_MultiTable_ShouldReturnPerTableResults()
    {
        // 1) 构造含 2 个表格的 docx
        var docxBytes = CreateMultiTableDocxBytes(
            new[] { new[] { "项目", "规格", "验收", "备注" }, new[] { "P1", "S1", "", "" } },
            new[] { new[] { "项目", "规格", "验收", "备注" }, new[] { "P2", "S2", "", "" } }
        );

        // 2) 上传
        var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(docxBytes), "file", "batch-preview.docx");
        var uploadResp = await _client.PostAsync("/api/documents/upload", multipart);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();

        // 3) 创建基础数据
        var customerId = (await (await _client.PostAsync("/api/customers",
            ApiClientJson.ToJsonContent(new { name = "BatchPrev-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processId = (await (await _client.PostAsync("/api/processes",
            ApiClientJson.ToJsonContent(new { name = "BatchPrev-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        { customerId, processId, project = "P1", specification = "S1", acceptance = "AC-1", remark = "R1" }));
        await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        { customerId, processId, project = "P2", specification = "S2", acceptance = "AC-2", remark = "R2" }));

        // 4) 批量预览
        var batchResp = await _client.PostAsync("/api/matching/batch-preview",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                tables = new[]
                {
                    new { tableIndex = 0, projectColumnIndex = 0, specificationColumnIndex = 1, acceptanceColumnIndex = 2, remarkColumnIndex = 3 },
                    new { tableIndex = 1, projectColumnIndex = 0, specificationColumnIndex = 1, acceptanceColumnIndex = 2, remarkColumnIndex = 3 }
                },
                customerId,
                processId,
                config = new { minScoreThreshold = 0.0 }
            }));

        batchResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var batchJson = await batchResp.ReadAsAsync<ApiResponse<JsonElement>>();
        batchJson.Code.Should().Be(0);

        var tables = batchJson.Data.GetProperty("tables");
        tables.GetArrayLength().Should().Be(2);

        // 每个表格应各有 1 个匹配项
        tables[0].GetProperty("items").GetArrayLength().Should().Be(1);
        tables[1].GetProperty("items").GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// 批量执行填充并下载：两个表格的验收列均被正确写入
    /// </summary>
    [Fact]
    public async Task BatchExecuteAndDownload_ShouldFillAllTables()
    {
        // 1) 构造含 2 个表格的 docx
        var docxBytes = CreateMultiTableDocxBytes(
            new[] { new[] { "项目", "规格", "验收", "备注" }, new[] { "PA", "SA", "", "" } },
            new[] { new[] { "项目", "规格", "验收", "备注" }, new[] { "PB", "SB", "", "" } }
        );

        // 2) 上传
        var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(docxBytes), "file", "batch-exec.docx");
        var uploadResp = await _client.PostAsync("/api/documents/upload", multipart);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();

        // 3) 创建基础数据
        var customerId = (await (await _client.PostAsync("/api/customers",
            ApiClientJson.ToJsonContent(new { name = "BatchExec-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processId = (await (await _client.PostAsync("/api/processes",
            ApiClientJson.ToJsonContent(new { name = "BatchExec-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var spec1Resp = await (await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        { customerId, processId, project = "PA", specification = "SA", acceptance = "FILL-A", remark = "REM-A" })))
            .ReadAsAsync<ApiResponse<JsonElement>>();
        var specIdA = spec1Resp.Data.GetProperty("id").GetInt32();

        var spec2Resp = await (await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        { customerId, processId, project = "PB", specification = "SB", acceptance = "FILL-B", remark = "REM-B" })))
            .ReadAsAsync<ApiResponse<JsonElement>>();
        var specIdB = spec2Resp.Data.GetProperty("id").GetInt32();

        // 4) 批量执行填充
        var execResp = await _client.PostAsync("/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                tables = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        acceptanceColumnIndex = 2,
                        remarkColumnIndex = 3,
                        mappings = new[] { new { rowIndex = 1, specId = specIdA } }
                    },
                    new
                    {
                        tableIndex = 1,
                        acceptanceColumnIndex = 2,
                        remarkColumnIndex = 3,
                        mappings = new[] { new { rowIndex = 1, specId = specIdB } }
                    }
                }
            }));

        execResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(0);
        execJson.Data.GetProperty("filledCount").GetInt32().Should().Be(2);
        var taskId = execJson.Data.GetProperty("taskId").GetString();

        // 5) 下载并验证两个表格内容
        var dlResp = await _client.GetAsync($"/api/matching/download/{taskId}");
        dlResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var filledBytes = await dlResp.Content.ReadAsByteArrayAsync();

        // 表格1 行1
        GetCellText(filledBytes, 0, 1, 2).Should().Be("FILL-A");
        GetCellText(filledBytes, 0, 1, 3).Should().Be("REM-A");

        // 表格2 行1
        GetCellText(filledBytes, 1, 1, 2).Should().Be("FILL-B");
        GetCellText(filledBytes, 1, 1, 3).Should().Be("REM-B");
    }

    /// <summary>
    /// Core 层单测：WriteMultipleTablesAsync 应同时写入两个表格
    /// </summary>
    [Fact]
    public async Task WriteMultipleTablesAsync_ShouldWriteAllTables()
    {
        var docxBytes = CreateMultiTableDocxBytes(
            new[] { new[] { "H1", "H2" }, new[] { "", "" } },
            new[] { new[] { "H3", "H4" }, new[] { "", "" } }
        );

        var writer = new WordDocumentWriter();

        // 使用可扩展的 MemoryStream（从 byte[] 构造的 MemoryStream 不可扩展）
        using var stream = new MemoryStream();
        stream.Write(docxBytes, 0, docxBytes.Length);
        stream.Position = 0;

        var tableOperations = new Dictionary<int, List<CellWriteOperation>>
        {
            [0] = new()
            {
                new CellWriteOperation { RowIndex = 1, ColumnIndex = 0, Value = "T1-R1C0" },
                new CellWriteOperation { RowIndex = 1, ColumnIndex = 1, Value = "T1-R1C1" }
            },
            [1] = new()
            {
                new CellWriteOperation { RowIndex = 1, ColumnIndex = 0, Value = "T2-R1C0" },
                new CellWriteOperation { RowIndex = 1, ColumnIndex = 1, Value = "T2-R1C1" }
            }
        };

        var count = await writer.WriteMultipleTablesAsync(stream, tableOperations);
        count.Should().Be(4);

        // 验证写入内容
        var resultBytes = stream.ToArray();
        GetCellText(resultBytes, 0, 1, 0).Should().Be("T1-R1C0");
        GetCellText(resultBytes, 0, 1, 1).Should().Be("T1-R1C1");
        GetCellText(resultBytes, 1, 1, 0).Should().Be("T2-R1C0");
        GetCellText(resultBytes, 1, 1, 1).Should().Be("T2-R1C1");
    }

    #region Helpers

    /// <summary>
    /// 创建包含多个表格的 docx
    /// </summary>
    private static byte[] CreateMultiTableDocxBytes(params string[][] [] tables)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;

            foreach (var rows in tables)
            {
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

                body.Append(table);
                // 表格之间加段落分隔
                body.Append(new Paragraph());
            }

            main.Document.Save();
        }

        return ms.ToArray();
    }

    private static string GetCellText(byte[] docx, int tableIndex, int rowIndex, int colIndex)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var allTables = body.Descendants<Table>().ToList();
        var table = allTables[tableIndex];
        var row = table.Elements<TableRow>().ToList()[rowIndex];
        var cell = row.Elements<TableCell>().ToList()[colIndex];
        return cell.InnerText ?? string.Empty;
    }

    #endregion
}
