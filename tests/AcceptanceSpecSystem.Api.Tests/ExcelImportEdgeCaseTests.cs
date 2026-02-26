using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using ClosedXML.Excel;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

/// <summary>
/// Excel 导入边缘场景测试
/// </summary>
public class ExcelImportEdgeCaseTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ExcelImportEdgeCaseTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Import_WithEmptyDataRows_ShouldReturnZeroSuccess()
    {
        var customerId = await CreateCustomerAsync("EdgeCase-Empty-C");
        var processId = await CreateProcessAsync("EdgeCase-Empty-P");

        // 构造只有表头、无数据行的 Excel
        // 注意：usedRange 只有 1 行，dataStartRow=2 超出 usedRange 会触发边界校验
        byte[] xlsxBytes;
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "项目";
            ws.Cell(1, 2).Value = "规格内容";
            ws.Cell(1, 3).Value = "验收标准";
            ws.Cell(1, 4).Value = "备注";
            // 在第 2 行写入空白值（非空字符串），确保 usedRange 覆盖到第 2 行
            // 空字符串不被 ClosedXML 视为有内容，空格字符可以
            ws.Cell(2, 1).Value = " ";
            ws.Cell(2, 2).Value = " ";

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            xlsxBytes = ms.ToArray();
        }

        var fileId = await UploadExcelAsync(xlsxBytes, "empty_data.xlsx");

        // 导入
        var importPayload = new
        {
            fileId,
            sheetIndex = 0,
            customerId,
            processId,
            headerRowStart = 1,
            headerRowCount = 1,
            dataStartRow = 2,
            projectColumn = 1,
            specificationColumn = 2,
            acceptanceColumn = 3,
            remarkColumn = 4
        };

        var importResp = await _client.PostAsync(
            "/api/documents/excel/import",
            new StringContent(JsonSerializer.Serialize(importPayload), Encoding.UTF8, "application/json"));
        importResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var importJson = await importResp.ReadAsAsync<ApiResponse<JsonElement>>();
        importJson.Code.Should().Be(0);
        // 只有空行，项目和规格都为空被跳过
        importJson.Data.GetProperty("successCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Import_WithBlankRowsInData_ShouldSkipBlanks()
    {
        var customerId = await CreateCustomerAsync("EdgeCase-Blank-C");
        var processId = await CreateProcessAsync("EdgeCase-Blank-P");

        // 构造数据区中间插入全空行的 Excel
        byte[] xlsxBytes;
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            // 表头行
            ws.Cell(1, 1).Value = "项目";
            ws.Cell(1, 2).Value = "规格内容";
            ws.Cell(1, 3).Value = "验收标准";
            ws.Cell(1, 4).Value = "备注";

            // 数据行1
            ws.Cell(2, 1).Value = "P1";
            ws.Cell(2, 2).Value = "S1";
            ws.Cell(2, 3).Value = "A1";
            ws.Cell(2, 4).Value = "R1";

            // 第3行全空（空行）
            // ws.Cell(3, 1) 不写任何内容

            // 数据行2（第4行）
            ws.Cell(4, 1).Value = "P2";
            ws.Cell(4, 2).Value = "S2";
            ws.Cell(4, 3).Value = "A2";
            ws.Cell(4, 4).Value = "R2";

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            xlsxBytes = ms.ToArray();
        }

        var fileId = await UploadExcelAsync(xlsxBytes, "blank_rows.xlsx");

        var importPayload = new
        {
            fileId,
            sheetIndex = 0,
            customerId,
            processId,
            headerRowStart = 1,
            headerRowCount = 1,
            dataStartRow = 2,
            projectColumn = 1,
            specificationColumn = 2,
            acceptanceColumn = 3,
            remarkColumn = 4
        };

        var importResp = await _client.PostAsync(
            "/api/documents/excel/import",
            new StringContent(JsonSerializer.Serialize(importPayload), Encoding.UTF8, "application/json"));
        importResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var importJson = await importResp.ReadAsAsync<ApiResponse<JsonElement>>();
        importJson.Code.Should().Be(0);

        // 只有2条有效数据行被导入（空行被跳过）
        importJson.Data.GetProperty("successCount").GetInt32().Should().Be(2);
    }

    private async Task<int> UploadExcelAsync(byte[] xlsxBytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(xlsxBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var uploadResp = await _client.PostAsync("/api/documents/upload", content);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        uploadJson.Code.Should().Be(0);
        return uploadJson.Data.GetProperty("fileId").GetInt32();
    }

    private async Task<int> CreateCustomerAsync(string name)
    {
        var resp = await _client.PostAsync(
            "/api/customers",
            new StringContent(JsonSerializer.Serialize(new { name }), Encoding.UTF8, "application/json"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        return json.Data.GetProperty("id").GetInt32();
    }

    private async Task<int> CreateProcessAsync(string name)
    {
        var resp = await _client.PostAsync(
            "/api/processes",
            new StringContent(JsonSerializer.Serialize(new { name }), Encoding.UTF8, "application/json"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        return json.Data.GetProperty("id").GetInt32();
    }
}
