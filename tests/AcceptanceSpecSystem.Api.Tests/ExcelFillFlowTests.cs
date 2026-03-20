using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using ClosedXML.Excel;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

/// <summary>
/// Excel 智能填充端到端测试
/// </summary>
public class ExcelFillFlowTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ExcelFillFlowTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Upload_Preview_Execute_ForExcel_ShouldWriteBackToSourceFile()
    {
        // 1) 构造 Excel（项目/规格/验收/备注）
        var originalXlsx = CreateExcelBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" },
            new[] { "P2", "S2", "", "" }
        });

        // 2) 上传 Excel
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(originalXlsx);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "e2e.xlsx");

        var uploadResp = await _client.PostAsync("/api/documents/upload", content);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        uploadJson.Code.Should().Be(0);
        uploadJson.Data.GetProperty("fileType").GetInt32().Should().Be(1);
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();

        // 3) 准备匹配数据
        var customerId = (await (await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name = "ExcelFill-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processId = (await (await _client.PostAsync("/api/processes", ApiClientJson.ToJsonContent(new { name = "ExcelFill-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "P1",
            specification = "S1",
            acceptance = "AC-1",
            remark = "RM-1"
        }));
        await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "P2",
            specification = "S2",
            acceptance = "AC-2",
            remark = "RM-2"
        }));

        // 4) 匹配预览
        var previewResp = await _client.PostAsync("/api/matching/preview", ApiClientJson.ToJsonContent(new
        {
            fileId,
            tableIndex = 0,
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            customerId,
            processId,
            config = new { minScoreThreshold = 0.0 }
        }));
        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);
        var items = previewJson.Data.GetProperty("items");
        items.GetArrayLength().Should().Be(2);

        var mappings = items.EnumerateArray().Select(i => new
        {
            rowIndex = i.GetProperty("rowIndex").GetInt32(),
            specId = i.GetProperty("bestMatch").GetProperty("specId").GetInt32(),
            matchScore = i.GetProperty("bestMatch").GetProperty("score").GetDouble()
        }).ToArray();

        // 5) 执行填充
        var execResp = await _client.PostAsync("/api/matching/execute", ApiClientJson.ToJsonContent(new
        {
            fileId,
            tableIndex = 0,
            acceptanceColumnIndex = 2,
            remarkColumnIndex = 3,
            highConfidenceThreshold = 0.95,
            mappings
        }));
        execResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(0);
        execJson.Data.GetProperty("filledCount").GetInt32().Should().Be(2);
        var taskId = execJson.Data.GetProperty("taskId").GetString();
        taskId.Should().NotBeNullOrWhiteSpace();
        execJson.Data.GetProperty("downloadUrl").GetString().Should().BeEmpty();

        // 6) 通过预览接口验证源 Excel 已被原地写回
        var previewAfterFillResp = await _client.GetAsync($"/api/documents/{fileId}/tables/0/preview?previewRows=0&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
        previewAfterFillResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewAfterFillJson = await previewAfterFillResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewAfterFillJson.Code.Should().Be(0);

        var rows = previewAfterFillJson.Data.GetProperty("rows");
        rows.GetArrayLength().Should().Be(2);
        rows[0][2].GetString().Should().Be("AC-1");
        rows[0][3].GetString().Should().Be("RM-1");
        rows[1][2].GetString().Should().Be("AC-2");
        rows[1][3].GetString().Should().Be("RM-2");
    }

    private static byte[] CreateExcelBytes(string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                sheet.Cell(r + 1, c + 1).Value = rows[r][c] ?? string.Empty;
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
