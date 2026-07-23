using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

/// <summary>
/// 文件对比功能集成测试
/// </summary>
public class FileCompareTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public FileCompareTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    /// <summary>
    /// Word 对比：相同文件应返回全部 Unchanged
    /// </summary>
    [Fact]
    public async Task WordCompare_IdenticalFiles_ShouldReturnAllUnchanged()
    {
        // 构造包含段落的 docx
        var docxBytes = CreateWordDocxBytes("Hello", "World", "Test");

        // 上传两份相同文件
        var (fileIdA, fileIdB) = await UploadCompareFilesAsync(docxBytes, "a.docx", docxBytes, "b.docx");

        // 调用 preview
        var previewResp = await _client.PostAsync(
            "/api/file-compare/preview",
            ApiClientJson.ToJsonContent(new { fileIdA, fileIdB }));
        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);

        // 相同文件：无新增/删除/修改
        previewJson.Data.GetProperty("addedCount").GetInt32().Should().Be(0);
        previewJson.Data.GetProperty("removedCount").GetInt32().Should().Be(0);
        previewJson.Data.GetProperty("modifiedCount").GetInt32().Should().Be(0);
        previewJson.Data.GetProperty("unchangedCount").GetInt32().Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Word 对比：不同文件应检测差异
    /// LCS 回溯顺序可能产生 Modified（Remove+Add 相邻）或分离的 Added+Removed，
    /// 因此断言"有变化"而非假定具体 diff 类型
    /// </summary>
    [Fact]
    public async Task WordCompare_DifferentFiles_ShouldDetectDifferences()
    {
        var docxA = CreateWordDocxBytes("Hello", "World");
        var docxB = CreateWordDocxBytes("Hello", "Changed");

        var (fileIdA, fileIdB) = await UploadCompareFilesAsync(docxA, "a.docx", docxB, "b.docx");

        var previewResp = await _client.PostAsync(
            "/api/file-compare/preview",
            ApiClientJson.ToJsonContent(new { fileIdA, fileIdB }));
        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);

        // "Hello" 未变
        previewJson.Data.GetProperty("unchangedCount").GetInt32().Should().Be(1);

        // "World" → "Changed"：LCS 可能产生 Modified 或 Added+Removed
        var modified = previewJson.Data.GetProperty("modifiedCount").GetInt32();
        var added = previewJson.Data.GetProperty("addedCount").GetInt32();
        var removed = previewJson.Data.GetProperty("removedCount").GetInt32();
        (modified + added + removed).Should().BeGreaterThan(0, "应检测到段落差异");

        var hunks = previewJson.Data.GetProperty("hunks");
        hunks.GetArrayLength().Should().BeGreaterThan(0, "有差异时应返回差异块");
        hunks[0].GetProperty("lines").GetArrayLength().Should().BeGreaterThan(0, "差异块应包含行");
    }

    /// <summary>
    /// Excel 对比：不同单元格应检测 Modified
    /// </summary>
    [Fact]
    public async Task ExcelCompare_DifferentCells_ShouldDetectModified()
    {
        var xlsxA = CreateExcelBytes(("A1", "X"), ("B1", "Y"));
        var xlsxB = CreateExcelBytes(("A1", "X"), ("B1", "Z"));

        var (fileIdA, fileIdB) = await UploadCompareFilesAsync(xlsxA, "a.xlsx", xlsxB, "b.xlsx");

        var previewResp = await _client.PostAsync(
            "/api/file-compare/preview",
            ApiClientJson.ToJsonContent(new { fileIdA, fileIdB }));
        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);

        // B1 从 Y 变为 Z，应有 Modified 项
        var items = previewJson.Data.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0);

        var hasModified = items.EnumerateArray()
            .Any(i => i.GetProperty("diffType").GetString() == "Modified");
        hasModified.Should().BeTrue("应检测到 B1 单元格的修改");

        var hunks = previewJson.Data.GetProperty("hunks");
        hunks.GetArrayLength().Should().BeGreaterThan(0, "有差异时应返回差异块");
    }

    [Fact]
    public async Task Preview_ExcludeUnchanged_ShouldKeepCompleteStatistics()
    {
        var xlsxA = CreateExcelBytes(("A1", "项目"), ("B1", "规格"), ("A2", "P1"), ("B2", "S1"));
        var xlsxB = CreateExcelBytes(("A1", "项目"), ("B1", "规格"), ("A2", "P1"), ("B2", "S2"));
        var (fileIdA, fileIdB) = await UploadCompareFilesAsync(xlsxA, "a.xlsx", xlsxB, "b.xlsx");

        var response = await _client.PostAsync(
            "/api/file-compare/preview",
            ApiClientJson.ToJsonContent(new { fileIdA, fileIdB, includeUnchanged = false }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var items = json.Data.GetProperty("items").EnumerateArray().ToList();
        items.Should().OnlyContain(item => item.GetProperty("diffType").GetString() != "Unchanged");
        json.Data.GetProperty("unchangedCount").GetInt32().Should().BeGreaterThan(0);
        json.Data.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(items.Count);
    }

    [Fact]
    public async Task TablePreview_Window_ShouldReturnOffsetsTotalsAndAlignedCells()
    {
        var cells = Enumerable.Range(1, 6)
            .SelectMany(row => Enumerable.Range(1, 5)
                .Select(column => ($"{ToExcelColumnName(column)}{row}", $"R{row}C{column}")))
            .ToArray();
        var bytes = CreateExcelBytes(cells);
        var (fileId, _) = await UploadCompareFilesAsync(bytes, "window-a.xlsx", bytes, "window-b.xlsx");

        var response = await _client.GetAsync(
            $"/api/documents/{fileId}/tables/0/preview?headerRowIndex=0&headerRowCount=1&dataStartRowIndex=0&rowOffset=2&previewRows=2&columnOffset=1&previewColumns=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Data.GetProperty("rowOffset").GetInt32().Should().Be(2);
        json.Data.GetProperty("columnOffset").GetInt32().Should().Be(1);
        json.Data.GetProperty("totalRows").GetInt32().Should().Be(6);
        json.Data.GetProperty("totalColumns").GetInt32().Should().Be(5);
        json.Data.GetProperty("columnCount").GetInt32().Should().Be(2);

        var rows = json.Data.GetProperty("rows");
        var structuredRows = json.Data.GetProperty("structuredRows");
        rows.GetArrayLength().Should().Be(2);
        structuredRows.GetArrayLength().Should().Be(rows.GetArrayLength());
        rows[0].GetArrayLength().Should().Be(2);
        structuredRows[0].GetArrayLength().Should().Be(rows[0].GetArrayLength());
        rows[0][0].GetString().Should().Be("R3C2");
    }

    [Fact]
    public async Task TablePreview_TailWindow_ShouldReturnOnlyRemainingCells()
    {
        var bytes = CreateExcelBytes(
            ("A1", "A1"), ("B1", "B1"), ("C1", "C1"),
            ("A2", "A2"), ("B2", "B2"), ("C2", "C2"),
            ("A3", "A3"), ("B3", "B3"), ("C3", "C3"));
        var (fileId, _) = await UploadCompareFilesAsync(bytes, "tail-a.xlsx", bytes, "tail-b.xlsx");

        var response = await _client.GetAsync(
            $"/api/documents/{fileId}/tables/0/preview?dataStartRowIndex=0&rowOffset=2&previewRows=200&columnOffset=2&previewColumns=60");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Data.GetProperty("rows").GetArrayLength().Should().Be(1);
        json.Data.GetProperty("rows")[0].GetArrayLength().Should().Be(1);
        json.Data.GetProperty("rows")[0][0].GetString().Should().Be("C3");
    }

    [Theory]
    [InlineData("previewRows=0")]
    [InlineData("previewRows=501")]
    [InlineData("previewRows=10&previewColumns=0")]
    [InlineData("previewRows=10&previewColumns=101")]
    [InlineData("rowOffset=-1&previewRows=200&columnOffset=0&previewColumns=60")]
    [InlineData("rowOffset=0&previewRows=501&columnOffset=0&previewColumns=60")]
    [InlineData("rowOffset=0&previewRows=200&columnOffset=-1&previewColumns=60")]
    [InlineData("rowOffset=0&previewRows=200&columnOffset=0&previewColumns=101")]
    public async Task TablePreview_InvalidWindow_ShouldReturn400(string query)
    {
        var bytes = CreateExcelBytes(("A1", "value"));
        var (fileId, _) = await UploadCompareFilesAsync(bytes, "invalid-a.xlsx", bytes, "invalid-b.xlsx");

        var response = await _client.GetAsync(
            $"/api/documents/{fileId}/tables/0/preview?dataStartRowIndex=0&{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// 验证校验：不同类型文件（.docx + .xlsx）应返回 400
    /// </summary>
    [Fact]
    public async Task Upload_DifferentFileTypes_ShouldReturn400()
    {
        var docxBytes = CreateWordDocxBytes("Hello");
        var xlsxBytes = CreateExcelBytes(("A1", "X"));

        using var multipart = new MultipartFormDataContent();

        var contentA = new ByteArrayContent(docxBytes);
        contentA.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        multipart.Add(contentA, "fileA", "a.docx");

        var contentB = new ByteArrayContent(xlsxBytes);
        contentB.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        multipart.Add(contentB, "fileB", "b.xlsx");

        var resp = await _client.PostAsync("/api/file-compare/upload", multipart);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await resp.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().NotBe(0);
    }

    /// <summary>
    /// 下载端点应返回 JSON 内容
    /// </summary>
    [Fact]
    public async Task Download_ShouldReturnJsonFile()
    {
        var docxA = CreateWordDocxBytes("Alpha", "Beta");
        var docxB = CreateWordDocxBytes("Alpha", "Gamma");

        var (fileIdA, fileIdB) = await UploadCompareFilesAsync(docxA, "a.docx", docxB, "b.docx");

        // 调用 download
        var downloadResp = await _client.PostAsync(
            "/api/file-compare/download",
            ApiClientJson.ToJsonContent(new { fileIdA, fileIdB }));
        downloadResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 验证 Content-Type 为 application/json
        downloadResp.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        // 验证返回的 JSON 可正常反序列化
        var body = await downloadResp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Download_ShouldIgnorePreviewFilterAndContainUnchangedItems()
    {
        var docxA = CreateWordDocxBytes("Same", "Old");
        var docxB = CreateWordDocxBytes("Same", "New");
        var (fileIdA, fileIdB) = await UploadCompareFilesAsync(docxA, "full-a.docx", docxB, "full-b.docx");

        var response = await _client.PostAsync(
            "/api/file-compare/download",
            ApiClientJson.ToJsonContent(new { fileIdA, fileIdB, includeUnchanged = false }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("items").EnumerateArray()
            .Should().Contain(item => item.GetProperty("diffType").GetString() == "Unchanged");
    }

    #region 辅助方法

    /// <summary>
    /// 上传两份对比文件，返回 (fileIdA, fileIdB)
    /// </summary>
    private async Task<(int fileIdA, int fileIdB)> UploadCompareFilesAsync(
        byte[] bytesA, string nameA, byte[] bytesB, string nameB)
    {
        using var multipart = new MultipartFormDataContent();

        var contentA = new ByteArrayContent(bytesA);
        var ext = Path.GetExtension(nameA).ToLowerInvariant();
        contentA.Headers.ContentType = new MediaTypeHeaderValue(
            ext == ".xlsx"
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        multipart.Add(contentA, "fileA", nameA);

        var contentB = new ByteArrayContent(bytesB);
        contentB.Headers.ContentType = new MediaTypeHeaderValue(
            ext == ".xlsx"
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        multipart.Add(contentB, "fileB", nameB);

        var uploadResp = await _client.PostAsync("/api/file-compare/upload", multipart);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        uploadJson.Code.Should().Be(0);

        var fileIdA = uploadJson.Data.GetProperty("fileA").GetProperty("fileId").GetInt32();
        var fileIdB = uploadJson.Data.GetProperty("fileB").GetProperty("fileId").GetInt32();
        return (fileIdA, fileIdB);
    }

    /// <summary>
    /// 构造包含指定段落的 Word 文档
    /// </summary>
    private static byte[] CreateWordDocxBytes(params string[] paragraphs)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;

            foreach (var text in paragraphs)
            {
                body.AppendChild(new Paragraph(new Run(new Text(text))));
            }

            main.Document.Save();
        }
        return ms.ToArray();
    }

    /// <summary>
    /// 构造包含指定单元格的 Excel 文档
    /// </summary>
    private static byte[] CreateExcelBytes(params (string cell, string value)[] cells)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        foreach (var (cell, value) in cells)
        {
            ws.Cell(cell).Value = value;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string ToExcelColumnName(int columnNumber)
    {
        var dividend = columnNumber;
        var columnName = string.Empty;
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            columnName = (char)('A' + modulo) + columnName;
            dividend = (dividend - modulo) / 26;
        }
        return columnName;
    }

    #endregion
}
