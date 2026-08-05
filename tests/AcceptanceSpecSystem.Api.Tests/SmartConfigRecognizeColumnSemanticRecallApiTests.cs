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

public class SmartConfigRecognizeColumnSemanticRecallApiTests
{
    [Fact]
    public async Task Recognize_WhenRuleMappingIsComplete_ShouldNotCallColumnSemanticRecall()
    {
        using var factory = new ColumnSemanticRecallCountingApiFactory();
        var client = factory.CreateClient();
        CountingColumnSemanticRecallService.Reset();
        var fileId = await UploadExcelAsync(client, CreateExcelBytes(), "smart-recognize-column-recall-complete.xlsx");

        var response = await client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();
        CountingColumnSemanticRecallService.CallCount.Should().Be(0);
        GetSemanticRecallSuggestions(table).Should().BeEmpty();
        var aiAssist = body.Data.GetProperty("aiAssist");
        aiAssist.GetProperty("requested").GetBoolean().Should().BeTrue();
        aiAssist.GetProperty("status").GetString().Should().Be("notNeeded");
        aiAssist.GetProperty("attemptedCalls").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Recognize_WhenSpecificationHeaderOnlyHasSemanticAlias_ShouldReturnSuggestionWithoutChangingRuleColumns()
    {
        using var factory = new ColumnSemanticRecallMissingSpecificationApiFactory();
        var client = factory.CreateClient();
        var fileId = await UploadExcelAsync(client, CreateSemanticAliasExcelBytes(), "smart-recognize-column-recall-spec.xlsx");

        var response = await client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();
        var suggestions = GetSemanticRecallSuggestions(table);

        table.GetProperty("specificationColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
        suggestions.Should().ContainSingle();
        var suggestion = suggestions.Single();
        suggestion.GetProperty("columnIndex").GetInt32().Should().Be(1);
        suggestion.GetProperty("header").GetString().Should().Be("管控要求");
        suggestion.GetProperty("targetField").GetString().Should().Be("Specification");
        suggestion.GetProperty("source").GetString().Should().Be("SemanticRecall");
        suggestion.GetProperty("confidence").GetDouble().Should().BeApproximately(0.88, 0.001);
        var aiAssist = body.Data.GetProperty("aiAssist");
        aiAssist.GetProperty("status").GetString().Should().Be("applied");
        aiAssist.GetProperty("attemptedCalls").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        aiAssist.GetProperty("successfulCalls").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Recognize_WhenAcceptanceMethodAndConfirmationHeadersExist_ShouldNotSuggestMethodAsAcceptance()
    {
        using var factory = new ColumnSemanticRecallMissingAcceptanceApiFactory();
        var client = factory.CreateClient();
        var fileId = await UploadExcelAsync(client, CreateAcceptanceMethodExcelBytes(), "smart-recognize-column-recall-acceptance.xlsx");

        var response = await client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();
        var suggestions = GetSemanticRecallSuggestions(table);

        suggestions.Should().ContainSingle();
        suggestions.Single().GetProperty("header").GetString().Should().Be("确认结果");
        suggestions.Single().GetProperty("targetField").GetString().Should().Be("Acceptance");
        suggestions.Should().NotContain(item => item.GetProperty("header").GetString() == "验收方式");
        table.GetProperty("acceptanceColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
    }

    [Fact]
    public async Task Recognize_WhenColumnSemanticRecallFailsOrReturnsInvalidField_ShouldKeepRuleResult()
    {
        using var factory = new ColumnSemanticRecallInvalidResultApiFactory();
        var client = factory.CreateClient();
        var fileId = await UploadExcelAsync(client, CreateSemanticAliasExcelBytes(), "smart-recognize-column-recall-invalid.xlsx");

        var response = await client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("specificationColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
        GetSemanticRecallSuggestions(table).Should().BeEmpty();
        var aiAssist = body.Data.GetProperty("aiAssist");
        aiAssist.GetProperty("status").GetString().Should().Be("fallback");
        aiAssist.GetProperty("reason").GetString().Should().Be("invalidOutput");
    }

    [Fact]
    public async Task Recognize_WhenRepeatedHeaderShapesNeedSemanticRecall_ShouldReuseRecallResultWithinDocument()
    {
        using var factory = new ColumnSemanticRecallRepeatedHeaderApiFactory();
        var client = factory.CreateClient();
        CountingColumnSemanticRecallService.Reset();
        var fileId = await UploadExcelAsync(
            client,
            CreateRepeatedSemanticAliasExcelBytes(),
            "smart-recognize-column-recall-repeated.xlsx");

        var response = await client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Data.GetProperty("tables").EnumerateArray().Should().HaveCount(3);
        CountingColumnSemanticRecallService.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Recognize_WhenRepeatedHeaderSemanticRecallFails_ShouldNotRetrySameHeaderShape()
    {
        using var factory = new ColumnSemanticRecallFailingRepeatedHeaderApiFactory();
        var client = factory.CreateClient();
        FailingColumnSemanticRecallService.Reset();
        var fileId = await UploadExcelAsync(
            client,
            CreateRepeatedSemanticAliasExcelBytes(),
            "smart-recognize-column-recall-repeated-fail.xlsx");

        var response = await client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Data.GetProperty("tables").EnumerateArray().Should().HaveCount(3);
        FailingColumnSemanticRecallService.CallCount.Should().Be(1);
        var aiAssist = body.Data.GetProperty("aiAssist");
        aiAssist.GetProperty("status").GetString().Should().Be("fallback");
        aiAssist.GetProperty("reason").GetString().Should().Be("callFailed");
        aiAssist.GetProperty("fallbackCalls").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Recognize_WhenColumnSemanticRecallHangs_ShouldUseIndependentRecallTimeout()
    {
        using var factory = new ColumnSemanticRecallTimeoutApiFactory();
        var client = factory.CreateClient();
        BlockingColumnSemanticRecallService.Reset();
        var fileId = await UploadExcelAsync(
            client,
            CreateSemanticAliasExcelBytes(),
            "smart-recognize-column-recall-timeout.xlsx");

        var response = await client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        BlockingColumnSemanticRecallService.WasCancelled.Should().BeTrue();
        BlockingColumnSemanticRecallService.CancelledAfter.Should().BeLessThan(TimeSpan.FromSeconds(2));
        body.Data.GetProperty("aiAssist").GetProperty("reason").GetString().Should().Be("timeout");
    }

    private static IReadOnlyList<JsonElement> GetSemanticRecallSuggestions(JsonElement table)
    {
        return table.TryGetProperty("semanticRecallSuggestions", out var suggestions) &&
               suggestions.ValueKind == JsonValueKind.Array
            ? suggestions.EnumerateArray().ToList()
            : [];
    }

    private static Task<int> UploadExcelAsync(HttpClient client, byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(client, bytes, fileName);

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格内容";
        worksheet.Cell(1, 3).Value = "验收结果";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateSemanticAliasExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "管控要求";
        worksheet.Cell(1, 3).Value = "验收结果";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "表面不得有明显划伤";
        worksheet.Cell(2, 3).Value = "OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateAcceptanceMethodExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格内容";
        worksheet.Cell(1, 3).Value = "验收方式";
        worksheet.Cell(1, 4).Value = "确认结果";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "表面不得有明显划伤";
        worksheet.Cell(2, 3).Value = "目视";
        worksheet.Cell(2, 4).Value = "OK";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateRepeatedSemanticAliasExcelBytes()
    {
        using var workbook = new XLWorkbook();
        for (var sheetIndex = 1; sheetIndex <= 3; sheetIndex++)
        {
            var worksheet = workbook.AddWorksheet($"验收表{sheetIndex}");
            worksheet.Cell(1, 1).Value = "项目";
            worksheet.Cell(1, 2).Value = "管控要求";
            worksheet.Cell(1, 3).Value = "验收结果";
            worksheet.Cell(1, 4).Value = "备注";
            worksheet.Cell(2, 1).Value = $"外观{sheetIndex}";
            worksheet.Cell(2, 2).Value = "表面不得有明显划伤";
            worksheet.Cell(2, 3).Value = "OK";
            worksheet.Cell(2, 4).Value = "抽检";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
