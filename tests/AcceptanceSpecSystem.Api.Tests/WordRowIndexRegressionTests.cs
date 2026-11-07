using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

/// <summary>
/// Word 行号回归：改为复用上传文档解析快照（6363756）后，快照投影的数据行号从 0 开始，
/// 预览返回的 rowIndex 与执行端校验/写回使用的表格物理行号相差一行，导致执行报
/// “不属于已确认的数据区域”或写错行。这里固定：预览 rowIndex = 表格物理行号（0 基，含表头），
/// 且执行按该行号写回，表头和标题行保持不变。
/// </summary>
public sealed class WordRowIndexRegressionTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public WordRowIndexRegressionTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Preview_WithDefaultHeader_ShouldReturnPhysicalWordRowIndexes()
    {
        var fileId = await UploadDocxAsync(
            "word-row-default-header.docx",
            ["项目", "规格", "验收", "备注"],
            ["WR-P1", "WR-S1", "", ""],
            ["WR-P2", "WR-S2", "", ""]);
        var (customerId, processId) = await CreateScopeAsync("WordRowDefault");
        await CreateSpecAsync(customerId, processId, "WR-P1", "WR-S1", "AC-1", "RM-1");
        await CreateSpecAsync(customerId, processId, "WR-P2", "WR-S2", "AC-2", "RM-2");

        var items = await PreviewItemsAsync(fileId, customerId, processId, tableRange: null);

        items.EnumerateArray()
            .Select(item => (item.GetProperty("rowIndex").GetInt32(), item.GetProperty("sourceProject").GetString()))
            .Should().Equal((1, "WR-P1"), (2, "WR-P2"));
    }

    [Fact]
    public async Task PreviewAndExecute_WithTitleRowAboveHeader_ShouldWriteToPreviewedPhysicalRows()
    {
        var fileId = await UploadDocxAsync(
            "word-row-title-header.docx",
            ["验收规格表", "", "", ""],
            ["项目", "规格", "验收", "备注"],
            ["WT-P1", "WT-S1", "", ""],
            ["WT-P2", "WT-S2", "", ""]);
        var (customerId, processId) = await CreateScopeAsync("WordRowTitle");
        await CreateSpecAsync(customerId, processId, "WT-P1", "WT-S1", "AC-1", "RM-1");
        await CreateSpecAsync(customerId, processId, "WT-P2", "WT-S2", "AC-2", "RM-2");

        // 行范围为 1 基：第 2 行是表头，第 3 行起是数据
        var tableRange = new { headerRowStart = 2, headerRowCount = 1, dataStartRow = 3 };
        var items = await PreviewItemsAsync(fileId, customerId, processId, tableRange);

        items.EnumerateArray()
            .Select(item => (item.GetProperty("rowIndex").GetInt32(), item.GetProperty("sourceProject").GetString()))
            .Should().Equal((2, "WT-P1"), (3, "WT-P2"));

        var mappings = items.EnumerateArray().Select(item => new
        {
            rowIndex = item.GetProperty("rowIndex").GetInt32(),
            specId = item.GetProperty("bestMatch").GetProperty("specId").GetInt32()
        }).ToArray();

        using var execResp = await _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
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
                        tableRange.headerRowStart,
                        tableRange.headerRowCount,
                        tableRange.dataStartRow,
                        mappings
                    }
                }
            }));
        execResp.StatusCode.Should().Be(HttpStatusCode.OK, await execResp.Content.ReadAsStringAsync());
        var taskId = (await execResp.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("taskId").GetString();

        using var downloadResp = await _client.GetAsync($"/api/matching/download/{taskId}");
        downloadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var filled = await downloadResp.Content.ReadAsByteArrayAsync();

        var rows = ReadTableRows(filled);
        rows[0].Should().Equal("验收规格表", "", "", "");
        rows[1].Should().Equal("项目", "规格", "验收", "备注");
        rows[2].Should().Equal("WT-P1", "WT-S1", "AC-1", "RM-1");
        rows[3].Should().Equal("WT-P2", "WT-S2", "AC-2", "RM-2");
    }

    private async Task<JsonElement> PreviewItemsAsync(int fileId, int customerId, int processId, object? tableRange)
    {
        var table = new Dictionary<string, object?>
        {
            ["tableIndex"] = 0,
            ["projectColumnIndex"] = 0,
            ["specificationColumnIndex"] = 1,
            ["acceptanceColumnIndex"] = 2,
            ["remarkColumnIndex"] = 3
        };
        if (tableRange != null)
        {
            foreach (var property in tableRange.GetType().GetProperties())
            {
                table[property.Name] = property.GetValue(tableRange);
            }
        }

        using var previewResp = await _client.PostAsync(
            "/api/matching/batch-preview",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                customerId,
                processId,
                config = new { minScoreThreshold = 0.0 },
                tables = new[] { table }
            }));
        previewResp.StatusCode.Should().Be(HttpStatusCode.OK, await previewResp.Content.ReadAsStringAsync());
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        return previewJson.Data.GetProperty("tables")[0].GetProperty("items").Clone();
    }

    private async Task<int> UploadDocxAsync(string fileName, params string[][] rows)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(CreateDocxBytes(rows)), "file", fileName);
        await multipart.AddDefaultBusinessOrgUnitIdAsync(_client);
        using var uploadResp = await _client.PostAsync("/api/documents/upload", multipart);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("fileId").GetInt32();
    }

    private async Task<(int CustomerId, int ProcessId)> CreateScopeAsync(string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var customerId = await CreateReferenceDataAsync("customers", $"{prefix}-C-{suffix}");
        var processId = await CreateReferenceDataAsync("processes", $"{prefix}-P-{suffix}");
        return (customerId, processId);
    }

    private async Task<int> CreateReferenceDataAsync(string resource, string name)
    {
        using var response = await _client.PostAsync($"/api/{resource}", ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
    }

    private async Task CreateSpecAsync(
        int customerId,
        int processId,
        string project,
        string specification,
        string acceptance,
        string remark)
    {
        using var response = await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                businessOrgUnitId = await TestBusinessOrg.GetDefaultBusinessOrgUnitIdAsync(_client),
                customerId,
                processId,
                project,
                specification,
                acceptance,
                remark
            }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static byte[] CreateDocxBytes(string[][] rows)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
            var table = new Table(new TableProperties(new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));
            foreach (var row in rows)
            {
                var tableRow = new TableRow();
                foreach (var value in row)
                {
                    tableRow.AppendChild(new TableCell(new Paragraph(new Run(new Text(value)))));
                }

                table.AppendChild(tableRow);
            }

            mainPart.Document = new Document(new Body(table));
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static List<string[]> ReadTableRows(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var document = WordprocessingDocument.Open(stream, false);
        var table = document.MainDocumentPart!.Document!.Body!.Descendants<Table>().First();
        return table.Elements<TableRow>()
            .Select(row => row.Elements<TableCell>().Select(cell => cell.InnerText).ToArray())
            .ToList();
    }
}
