using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using ClosedXML.Excel;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class ExcelImportEndRowTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ExcelImportEndRowTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PreviewAndImport_ShouldRespectConfiguredDataEndRow()
    {
        var customerId = await CreateCustomerAsync("Excel-EndRow-C");
        var processId = await CreateProcessAsync("Excel-EndRow-P");

        byte[] xlsxBytes;
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "项目";
            ws.Cell(1, 2).Value = "规格内容";
            ws.Cell(1, 3).Value = "验收标准";
            ws.Cell(1, 4).Value = "备注";

            ws.Cell(2, 1).Value = "P1";
            ws.Cell(2, 2).Value = "S1";
            ws.Cell(2, 3).Value = "A1";
            ws.Cell(2, 4).Value = "R1";

            ws.Cell(3, 1).Value = "P2";
            ws.Cell(3, 2).Value = "S2";
            ws.Cell(3, 3).Value = "A2";
            ws.Cell(3, 4).Value = "R2";

            ws.Cell(4, 1).Value = "P3";
            ws.Cell(4, 2).Value = "S3";
            ws.Cell(4, 3).Value = "A3";
            ws.Cell(4, 4).Value = "R3";

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            xlsxBytes = ms.ToArray();
        }

        var fileId = await UploadExcelAsync(xlsxBytes, "end-row.xlsx");

        var previewResp = await _client.GetAsync(
            $"/api/documents/{fileId}/tables/0/preview?previewRows=500&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1&dataEndRowIndex=2");
        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);
        previewJson.Data.GetProperty("totalRows").GetInt32().Should().Be(2);
        previewJson.Data.GetProperty("rows").GetArrayLength().Should().Be(2);

        var importPayload = new
        {
            fileId,
            sheetIndex = 0,
            customerId,
            processId,
            headerRowStart = 1,
            headerRowCount = 1,
            dataStartRow = 2,
            dataEndRow = 3,
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
        importJson.Data.GetProperty("successCount").GetInt32().Should().Be(2);
        importJson.Data.GetProperty("failedCount").GetInt32().Should().Be(0);
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
