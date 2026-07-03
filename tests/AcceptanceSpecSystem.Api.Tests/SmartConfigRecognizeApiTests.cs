using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Documents.Models;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Api.Tests;

public class SmartConfigRecognizeApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeApiTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WithExcel_ShouldReturnFlatTables()
    {
        var customerId = await CreateCustomerAsync("智能识别-客户A");
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));

        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(0);
        body.Data.GetProperty("fileId").GetInt32().Should().Be(fileId);
        body.Data.GetProperty("tables").ValueKind.Should().Be(JsonValueKind.Array);

        var table = body.Data.GetProperty("tables").EnumerateArray().Single();
        table.GetProperty("tableIndex").GetInt32().Should().Be(0);
        table.GetProperty("tableName").GetString().Should().Be("验收表");
        table.GetProperty("headers").EnumerateArray().Select(item => item.GetString())
            .Should().Contain(new[] { "项目", "规格内容", "验收结果", "备注" });
        table.GetProperty("headerRowIndex").GetInt32().Should().Be(0);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(1);
        table.GetProperty("projectColumnIndex").GetInt32().Should().Be(0);
        table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(1);
        table.GetProperty("acceptanceColumnIndex").GetInt32().Should().Be(2);
        table.GetProperty("decision").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Recognize_WithExcelTitleBeforeTraditionalHeaders_ShouldDetectHeaderRowAndColumns()
    {
        var customerId = await CreateCustomerAsync("智能识别-繁体表头客户");
        var fileId = await UploadExcelAsync(
            CreateExcelWithTraditionalHeadersAfterTitleBytes(),
            "smart-recognize-traditional-title.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));

        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(0);

        var table = body.Data.GetProperty("tables").EnumerateArray().Single();
        table.GetProperty("headers").EnumerateArray().Select(item => item.GetString())
            .Should().Contain(new[] { "驗收項目", "驗收規格", "驗收方法", "設備商確認", "備註" });
        table.GetProperty("headerRowIndex").GetInt32().Should().Be(1);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(2);
        table.GetProperty("projectColumnIndex").GetInt32().Should().Be(1);
        table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(3);
        table.GetProperty("acceptanceColumnIndex").GetInt32().Should().Be(5);
        table.GetProperty("remarkColumnIndex").GetInt32().Should().Be(6);
    }

    [Fact]
    public async Task Recognize_WhenCustomerTemplateExists_ShouldUseTemplate()
    {
        var customerId = await CreateCustomerAsync("智能识别-客户B");
        await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            customerId,
            templateName = "模板命中",
            headers = new[] { "项目", "规格内容", "验收结果", "备注" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            acceptanceColumnIndex = 2,
            remarkColumnIndex = 3,
            headerRowIndex = 0,
            headerRowCount = 1,
            dataStartRowIndex = 1,
            isSpecificationOnly = false,
            learnedColumns = Array.Empty<object>()
        }));
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-template.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("Template");
        table.GetProperty("decision").GetString().Should().Be("AutoApply");
        table.GetProperty("confidence").GetDouble().Should().Be(1.0);
    }

    private async Task<int> CreateCustomerAsync(string name)
    {
        var response = await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("id").GetInt32();
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);

        var json = JsonSerializer.Deserialize<ApiResponse<JsonElement>>(
            responseText,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return json.Data.GetProperty("fileId").GetInt32();
    }

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
        worksheet.Cell(2, 3).Value = "目视 OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateExcelWithTraditionalHeadersAfterTitleBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Tray盤式投收板機驗收規格");
        worksheet.Cell(1, 1).Value = "Tray盤式投收板機驗收規格 單位：MSAP";
        worksheet.Range(1, 1, 1, 7).Merge();
        worksheet.Cell(2, 1).Value = "項次";
        worksheet.Cell(2, 2).Value = "驗收項目";
        worksheet.Cell(2, 4).Value = "驗收規格";
        worksheet.Cell(2, 5).Value = "驗收方法";
        worksheet.Cell(2, 6).Value = "設備商確認";
        worksheet.Cell(2, 7).Value = "備註";
        worksheet.Cell(3, 1).Value = "1";
        worksheet.Cell(3, 2).Value = "投收板機設備制程能力";
        worksheet.Cell(3, 3).Value = "設備流向";
        worksheet.Cell(3, 4).Value = "依主設備流向";
        worksheet.Cell(3, 5).Value = "裝機時檢查";
        worksheet.Cell(3, 6).Value = "■OK   □NG";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeHealthCheckApiTests : IClassFixture<MissingSpecificationColumnIntelligenceApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeHealthCheckApiTests(MissingSpecificationColumnIntelligenceApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenHighConfidenceResultMissesSpecificationColumn_ShouldNeedConfirm()
    {
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-missing-spec.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("confidence").GetDouble().Should().Be(0.96);
        table.GetProperty("specificationColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "验收结果";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "目视 OK";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeLlmFusionApiTests : IClassFixture<LlmFillsMissingSpecificationApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeLlmFusionApiTests(LlmFillsMissingSpecificationApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenRuleMissesSpecificationAndLlmFillsIt_ShouldReturnFusedAutoApply()
    {
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-llm-fusion.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("Fused");
        table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(1);
        table.GetProperty("decision").GetString().Should().Be("AutoApply");
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "验收结果";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "目视 OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeSpecificationOnlyApiTests : IClassFixture<SpecificationOnlyIntelligenceApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeSpecificationOnlyApiTests(SpecificationOnlyIntelligenceApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenHighConfidenceResultIsSpecificationOnly_ShouldReturnAutoApply()
    {
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-specification-only.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("projectColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(0);
        table.GetProperty("acceptanceColumnIndex").GetInt32().Should().Be(1);
        table.GetProperty("remarkColumnIndex").GetInt32().Should().Be(2);
        table.GetProperty("isSpecificationOnly").GetBoolean().Should().BeTrue();
        table.GetProperty("decision").GetString().Should().Be("AutoApply");
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "规格";
        worksheet.Cell(1, 2).Value = "验收标准";
        worksheet.Cell(1, 3).Value = "备注";
        worksheet.Cell(2, 1).Value = "无划伤";
        worksheet.Cell(2, 2).Value = "目视 OK";
        worksheet.Cell(2, 3).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeLlmRequiredColumnsApiTests : IClassFixture<LlmFillsMissingRequiredColumnsApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeLlmRequiredColumnsApiTests(LlmFillsMissingRequiredColumnsApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenRuleMissesAcceptanceAndRemarkAndLlmFillsThem_ShouldReturnFusedAutoApply()
    {
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-llm-required-columns.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("Fused");
        table.GetProperty("projectColumnIndex").GetInt32().Should().Be(0);
        table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(1);
        table.GetProperty("acceptanceColumnIndex").GetInt32().Should().Be(2);
        table.GetProperty("remarkColumnIndex").GetInt32().Should().Be(3);
        table.GetProperty("decision").GetString().Should().Be("AutoApply");
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "验收标准";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "目视 OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeLlmIncompleteApiTests : IClassFixture<LlmIncompleteRequiredColumnsApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeLlmIncompleteApiTests(LlmIncompleteRequiredColumnsApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenLlmStillMissesRequiredColumns_ShouldReturnFusedNeedConfirm()
    {
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-llm-incomplete.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("Fused");
        table.GetProperty("acceptanceColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("remarkColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "验收标准";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "目视 OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeLlmTimeoutApiTests : IClassFixture<LlmStructureTimeoutApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeLlmTimeoutApiTests(LlmStructureTimeoutApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenLlmStructureAdjudicationHangs_ShouldReturnRuleResultAfterBusinessTimeout()
    {
        BlockingStructureAdjudicationService.Reset();
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-llm-timeout.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("RuleBased");
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
        BlockingStructureAdjudicationService.WasCancelled.Should().BeTrue("业务超时应取消挂起的 LLM 调用");
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "验收结果";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "目视 OK";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public sealed class MissingSpecificationColumnIntelligenceApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.AddScoped<IDocumentIntelligenceService, MissingSpecificationColumnIntelligenceService>();
        });
    }
}

public sealed class LlmStructureTimeoutApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SmartConfiguration:StructureAdjudicationTimeoutSeconds"] = "1"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.AddScoped<IDocumentIntelligenceService, MissingSpecificationColumnIntelligenceService>();
            services.AddScoped<ILlmDocumentStructureAdjudicationService, BlockingStructureAdjudicationService>();
        });
    }
}

public sealed class LlmFillsMissingSpecificationApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.AddScoped<IDocumentIntelligenceService, FusableMissingSpecificationColumnIntelligenceService>();
            services.AddScoped<ILlmDocumentStructureAdjudicationService, FillSpecificationColumnStructureAdjudicationService>();
        });
    }
}

public sealed class SpecificationOnlyIntelligenceApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.AddScoped<IDocumentIntelligenceService, SpecificationOnlyIntelligenceService>();
        });
    }
}

public sealed class LlmFillsMissingRequiredColumnsApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.AddScoped<IDocumentIntelligenceService, MissingAcceptanceAndRemarkColumnIntelligenceService>();
            services.AddScoped<ILlmDocumentStructureAdjudicationService, FillRequiredColumnsStructureAdjudicationService>();
        });
    }
}

public sealed class LlmIncompleteRequiredColumnsApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.AddScoped<IDocumentIntelligenceService, MissingAcceptanceAndRemarkColumnIntelligenceService>();
            services.AddScoped<ILlmDocumentStructureAdjudicationService, IncompleteRequiredColumnsStructureAdjudicationService>();
        });
    }
}

public sealed class BlockingStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public static bool WasCancelled { get; private set; }

    public static void Reset()
    {
        WasCancelled = false;
    }

    public async Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WasCancelled = true;
            throw;
        }
    }
}

public sealed class MissingAcceptanceAndRemarkColumnIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.96,
            Reasoning = "高置信但缺验收列和备注列",
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = 1,
                AcceptanceColumn = null,
                RemarkColumn = null,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData) => 0;
}

public sealed class MissingSpecificationColumnIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.96,
            Reasoning = "高置信但缺规格列",
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = null,
                AcceptanceColumn = 1,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData) => 0;
}

public sealed class FusableMissingSpecificationColumnIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.96,
            Reasoning = "高置信但缺规格列，验收列已确定",
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = null,
                AcceptanceColumn = 2,
                RemarkColumn = 3,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData) => 0;
}

public sealed class SpecificationOnlyIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.96,
            Reasoning = "高置信仅规格结构",
            Mapping = new ColumnMapping
            {
                ProjectColumn = null,
                SpecificationColumn = 0,
                AcceptanceColumn = 1,
                RemarkColumn = 2,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData) => 0;
}

public sealed class FillSpecificationColumnStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.92,
            Decision = "autoApply",
            Reason = "测试替身补出规格列",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    SpecificationColumnIndex = 1,
                    Confidence = 0.92,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}

public sealed class FillRequiredColumnsStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.93,
            Decision = "autoApply",
            Reason = "测试替身补齐导入必填列",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    ProjectColumnIndex = 0,
                    SpecificationColumnIndex = 1,
                    AcceptanceColumnIndex = 2,
                    RemarkColumnIndex = 3,
                    Confidence = 0.93,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}

public sealed class IncompleteRequiredColumnsStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.91,
            Decision = "needConfirm",
            Reason = "测试替身仍无法确认验收列和备注列",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    ProjectColumnIndex = 0,
                    SpecificationColumnIndex = 1,
                    Confidence = 0.91,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}
