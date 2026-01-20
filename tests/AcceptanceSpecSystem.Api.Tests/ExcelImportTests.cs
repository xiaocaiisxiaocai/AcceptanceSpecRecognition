using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using ClosedXML.Excel;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class ExcelImportTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ExcelImportTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UploadExcel_GetSheets_Preview_And_Import_ShouldWork()
    {
        // 1) 创建客户与制程
        var customerId = await CreateCustomerAsync("测试客户-Excel");
        var processId = await CreateProcessAsync("测试制程-Excel");

        // 2) 构造 Excel（UsedRange 从 行2、列3 开始；表头2行；数据从第4行开始）
        byte[] xlsxBytes;
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet-A");

            // 组标题（跨列合并）
            ws.Cell(2, 3).Value = "基本信息";
            ws.Range(2, 3, 2, 4).Merge();
            ws.Cell(2, 5).Value = "要求";
            ws.Range(2, 5, 2, 6).Merge();

            // 表头行（第3行）
            ws.Cell(3, 3).Value = "项目";
            ws.Cell(3, 4).Value = "规格内容";
            ws.Cell(3, 5).Value = "验收标准";
            ws.Cell(3, 6).Value = "备注";

            // 数据区（项目跨行合并）
            ws.Cell(4, 3).Value = "P1";
            ws.Range(4, 3, 5, 3).Merge();
            ws.Cell(4, 4).Value = "S1";
            ws.Cell(4, 5).Value = "A1";
            ws.Cell(4, 6).Value = "R1";
            ws.Cell(5, 4).Value = "S2";
            ws.Cell(5, 5).Value = "A2";
            ws.Cell(5, 6).Value = "R2";

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            xlsxBytes = ms.ToArray();
        }

        // 3) 上传
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(xlsxBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "example.xlsx");

        var uploadResp = await _client.PostAsync("/api/documents/upload", content);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        uploadJson.Code.Should().Be(0);
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();
        fileId.Should().BeGreaterThan(0);
        uploadJson.Data.GetProperty("fileType").GetInt32().Should().Be(1);

        // 4) 获取工作表列表
        var tablesResp = await _client.GetAsync($"/api/documents/{fileId}/tables");
        tablesResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var tablesJson = await tablesResp.ReadAsAsync<ApiResponse<JsonElement>>();
        tablesJson.Code.Should().Be(0);
        tablesJson.Data.ValueKind.Should().Be(JsonValueKind.Array);
        var first = tablesJson.Data.EnumerateArray().First();
        first.GetProperty("name").GetString().Should().Be("Sheet-A");
        first.GetProperty("usedRangeStartRow").GetInt32().Should().Be(2);
        first.GetProperty("usedRangeStartColumn").GetInt32().Should().Be(3);

        // 5) 预览：表头起始=第2行，表头2行 => 对后端是 headerRowIndex=0, headerRowCount=2；数据起始=第4行 => dataStartRowIndex=2
        var previewResp = await _client.GetAsync($"/api/documents/{fileId}/tables/0/preview?previewRows=0&headerRowIndex=0&headerRowCount=2&dataStartRowIndex=2");
        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);
        previewJson.Data.GetProperty("totalRows").GetInt32().Should().Be(2);

        // 6) 导入：按列序号（绝对列号 3~6），行号 1-based
        var importPayload = new
        {
            fileId,
            sheetIndex = 0,
            customerId,
            processId,
            headerRowStart = 2,
            headerRowCount = 2,
            dataStartRow = 4,
            projectColumn = 3,
            specificationColumn = 4,
            acceptanceColumn = 5,
            remarkColumn = 6
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

    private async Task<int> CreateCustomerAsync(string name)
    {
        var payload = new { name };
        var resp = await _client.PostAsync(
            "/api/customers",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        return json.Data.GetProperty("id").GetInt32();
    }

    private async Task<int> CreateProcessAsync(string name)
    {
        var payload = new { name };
        var resp = await _client.PostAsync(
            "/api/processes",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        return json.Data.GetProperty("id").GetInt32();
    }
}

