using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Api.Tests;

public class SmartConfigRecognizeMultiHeaderApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeMultiHeaderApiTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenExcelHasTwoHeaderRows_ShouldReturnHeaderRowCountTwoAndDataStartAfterHeaders()
    {
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-multi-header.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(0);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(2);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(2);
        table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Recognize_WhenHeaderStartsAfterLeadingDescriptionRows_ShouldDetectFullHeaderBlock()
    {
        var fileId = await UploadExcelAsync(
            CreateLateMultiHeaderExcelBytes(),
            "smart-recognize-late-multi-header.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(4);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(3);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(7);
        table.GetProperty("projectColumnIndex").GetInt32().Should().Be(0);
        table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Recognize_WhenShortBusinessDescriptionPrecedesHeader_ShouldNotIncludeDescriptionAsHeader()
    {
        var fileId = await UploadExcelAsync(
            CreateShortBusinessDescriptionExcelBytes(),
            "smart-recognize-short-description-before-header.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(1);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(1);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(2);
        table.GetProperty("headers").EnumerateArray()
            .Select(item => item.GetString())
            .Should()
            .NotContain(header => header != null && header.Contains("客户A"));
    }

    [Fact]
    public async Task Recognize_WhenAdditionalHeaderUsesCustomerDomainWords_ShouldIncludeItAsHeader()
    {
        var fileId = await UploadExcelAsync(
            CreateCustomerDomainMultiHeaderExcelBytes(),
            "smart-recognize-customer-domain-multi-header.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(0);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(2);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(2);
        table.GetProperty("headers").EnumerateArray()
            .Select(item => item.GetString())
            .Should()
            .Contain(header => header == "基本信息 / 检查对象")
            .And.Contain(header => header == "判定依据 / 管制条件");
    }

    [Fact]
    public async Task Recognize_WhenAdditionalHeaderOnlyMatchesCustomerLearningWords_ShouldIncludeItAsHeader()
    {
        var customerId = await CreateCustomerAsync("表头学习词客户");
        await CreateColumnRuleAsync(customerId, "验货范围", targetField: 1);
        await CreateColumnRuleAsync(customerId, "承认条件", targetField: 2);
        await CreateColumnRuleAsync(customerId, "厂商回覆", targetField: 3);
        await CreateColumnRuleAsync(customerId, "附注", targetField: 4);
        var fileId = await UploadExcelAsync(
            CreateLearnedWordsMultiHeaderExcelBytes(),
            "smart-recognize-learned-words-multi-header.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(0);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(2);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(2);
        table.GetProperty("headers").EnumerateArray()
            .Select(item => item.GetString())
            .Should()
            .Contain(header => header == "基本信息 / 验货范围")
            .And.Contain(header => header == "判定依据 / 承认条件");
    }

    [Fact]
    public async Task Recognize_WhenMultiHeaderExcelHasNoDataRows_ShouldReturnNullEndRow()
    {
        var fileId = await UploadExcelAsync(
            CreateMultiHeaderNoDataRowExcelBytes(),
            "smart-recognize-multi-header-no-data.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        var dataStartRowIndex = table.GetProperty("dataStartRowIndex").GetInt32();
        dataStartRowIndex.Should().BeGreaterThan(0);
        table.GetProperty("dataEndRowIndex").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private Task<int> UploadExcelAsync(byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(_client, bytes, fileName);

    private async Task<int> CreateCustomerAsync(string name)
    {
        var response = await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("id").GetInt32();
    }

    private async Task CreateColumnRuleAsync(int customerId, string pattern, int targetField)
    {
        var response = await _client.PostAsync("/api/column-mapping-rules", ApiClientJson.ToJsonContent(new
        {
            pattern,
            targetField,
            matchMode = 2,
            priority = 100,
            enabled = true,
            source = 3,
            customerId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "基本信息";
        worksheet.Cell(1, 2).Value = "判定依据";
        worksheet.Cell(1, 3).Value = "验收信息";
        worksheet.Cell(1, 4).Value = "验收信息";
        worksheet.Cell(2, 1).Value = "项目";
        worksheet.Cell(2, 2).Value = "规格";
        worksheet.Cell(2, 3).Value = "验收标准";
        worksheet.Cell(2, 4).Value = "备注";
        worksheet.Cell(3, 1).Value = "外观";
        worksheet.Cell(3, 2).Value = "无划伤";
        worksheet.Cell(3, 3).Value = "目视 OK";
        worksheet.Cell(3, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateShortBusinessDescriptionExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "客户A";
        worksheet.Cell(1, 2).Value = "机种X";
        worksheet.Cell(1, 3).Value = "版本B";
        worksheet.Cell(1, 4).Value = "量产";
        worksheet.Cell(2, 1).Value = "项目";
        worksheet.Cell(2, 2).Value = "规格";
        worksheet.Cell(2, 3).Value = "验收标准";
        worksheet.Cell(2, 4).Value = "备注";
        worksheet.Cell(3, 1).Value = "外观";
        worksheet.Cell(3, 2).Value = "无划伤";
        worksheet.Cell(3, 3).Value = "目视 OK";
        worksheet.Cell(3, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateCustomerDomainMultiHeaderExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "基本信息";
        worksheet.Cell(1, 2).Value = "判定依据";
        worksheet.Cell(1, 3).Value = "回复信息";
        worksheet.Cell(1, 4).Value = "回复信息";
        worksheet.Cell(2, 1).Value = "检查对象";
        worksheet.Cell(2, 2).Value = "管制条件";
        worksheet.Cell(2, 3).Value = "供应商确认";
        worksheet.Cell(2, 4).Value = "补充说明";
        worksheet.Cell(3, 1).Value = "外观";
        worksheet.Cell(3, 2).Value = "表面不得有明显划伤";
        worksheet.Cell(3, 3).Value = "OK";
        worksheet.Cell(3, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateLearnedWordsMultiHeaderExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "基本信息";
        worksheet.Cell(1, 2).Value = "判定依据";
        worksheet.Cell(1, 3).Value = "回复信息";
        worksheet.Cell(1, 4).Value = "回复信息";
        worksheet.Cell(2, 1).Value = "验货范围";
        worksheet.Cell(2, 2).Value = "承认条件";
        worksheet.Cell(2, 3).Value = "厂商回覆";
        worksheet.Cell(2, 4).Value = "附注";
        worksheet.Cell(3, 1).Value = "外观";
        worksheet.Cell(3, 2).Value = "表面不得有明显划伤";
        worksheet.Cell(3, 3).Value = "OK";
        worksheet.Cell(3, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateMultiHeaderNoDataRowExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Utility");
        worksheet.Cell(1, 1).Value = "设备类型";
        worksheet.Cell(1, 2).Value = "电力需求";
        worksheet.Cell(1, 3).Value = "电力需求";
        worksheet.Cell(1, 4).Value = "其它需求";
        worksheet.Cell(2, 1).Value = "设备名称";
        worksheet.Cell(2, 2).Value = "电压";
        worksheet.Cell(2, 3).Value = "紧急电功率";
        worksheet.Cell(2, 4).Value = "备注";
        worksheet.Cell(3, 1).Value = "设备名称";
        worksheet.Cell(3, 2).Value = "电压";
        worksheet.Cell(3, 3).Value = "紧急电功率";
        worksheet.Cell(3, 4).Value = "备注";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateLateMultiHeaderExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "客户：A";
        worksheet.Cell(2, 1).Value = "文件编号：QA-001";
        worksheet.Cell(3, 1).Value = "以下为验收规格";
        worksheet.Cell(4, 1).Value = "请按实际项目确认";
        worksheet.Cell(5, 1).Value = "基本信息";
        worksheet.Cell(5, 2).Value = "规格信息";
        worksheet.Cell(5, 3).Value = "验收信息";
        worksheet.Cell(5, 4).Value = "验收信息";
        worksheet.Cell(6, 1).Value = "分类";
        worksheet.Cell(6, 2).Value = "判定依据";
        worksheet.Cell(6, 3).Value = "执行方式";
        worksheet.Cell(6, 4).Value = "补充说明";
        worksheet.Cell(7, 1).Value = "项目";
        worksheet.Cell(7, 2).Value = "规格";
        worksheet.Cell(7, 3).Value = "验收标准";
        worksheet.Cell(7, 4).Value = "备注";
        worksheet.Cell(8, 1).Value = "外观";
        worksheet.Cell(8, 2).Value = "无划伤";
        worksheet.Cell(8, 3).Value = "目视 OK";
        worksheet.Cell(8, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeWordHeaderApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeWordHeaderApiTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenWordHasCustomerDomainMultiHeader_ShouldReturnJoinedHeaders()
    {
        var fileId = await UploadWordAsync(
            CreateWordBytes([
                ["基本信息", "判定依据", "回复信息", "回复信息"],
                ["检查对象", "管制条件", "供应商确认", "补充说明"],
                ["外观", "表面不得有明显划伤", "OK", "抽检"]
            ]),
            "smart-recognize-word-customer-domain-multi-header.docx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));
        var responseText = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, responseText);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(0);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(2);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(2);
        table.GetProperty("headers").EnumerateArray()
            .Select(item => item.GetString())
            .Should()
            .Contain(header => header == "基本信息 / 检查对象")
            .And.Contain(header => header == "判定依据 / 管制条件");
    }

    private Task<int> UploadWordAsync(byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadWordAsync(_client, bytes, fileName);

    private static byte[] CreateWordBytes(string[][] rows) =>
        SmartConfigRecognizeTestFiles.CreateWordBytes(rows);
}
