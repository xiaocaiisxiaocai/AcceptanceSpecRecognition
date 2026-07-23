using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class SmartConfigRealSampleFactAttribute : FactAttribute
{
    private const string SamplePathEnvName = "SMART_CONFIG_REAL_SAMPLE_PATH";

    public SmartConfigRealSampleFactAttribute()
    {
        var path = Environment.GetEnvironmentVariable(SamplePathEnvName);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Skip = $"默认跳过真实样本验证。设置环境变量 {SamplePathEnvName} 指向 .xlsx/.docx 后再运行。";
        }
    }
}

public class SmartConfigRealSampleValidationTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string SamplePathEnvName = "SMART_CONFIG_REAL_SAMPLE_PATH";
    private readonly HttpClient _client;

    public SmartConfigRealSampleValidationTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [SmartConfigRealSampleFact]
    public async Task RecognizeAndConfirm_WithRealSample_ShouldProduceUsableStructure()
    {
        var samplePath = Environment.GetEnvironmentVariable(SamplePathEnvName)!;
        var customerId = await CreateCustomerAsync($"真实样本验证-{Guid.NewGuid():N}");
        var fileId = await UploadSampleAsync(samplePath);

        var recognizeResponse = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));
        var recognizeText = await recognizeResponse.Content.ReadAsStringAsync();
        recognizeResponse.StatusCode.Should().Be(HttpStatusCode.OK, recognizeText);

        var recognizeBody = await recognizeResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        recognizeBody.Code.Should().Be(0, recognizeText);
        var tables = recognizeBody.Data!.GetProperty("tables").EnumerateArray().ToList();
        tables.Should().NotBeEmpty("真实样本应至少识别出一个表格");

        var usableTable = tables.Single(table =>
            table.GetProperty("tableIndex").GetInt32() == 0);
        var headers = usableTable.GetProperty("headers")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
        var specificationColumnIndex = ReadNullableInt(usableTable, "specificationColumnIndex");
        specificationColumnIndex.Should().NotBeNull("真实样本确认保存至少需要规格列");

        var projectColumnIndex = ReadNullableInt(usableTable, "projectColumnIndex");
        var acceptanceColumnIndex = ReadNullableInt(usableTable, "acceptanceColumnIndex");
        var remarkColumnIndex = ReadNullableInt(usableTable, "remarkColumnIndex");
        var headerRowIndex = usableTable.GetProperty("headerRowIndex").GetInt32();
        var headerRowCount = usableTable.GetProperty("headerRowCount").GetInt32();
        var dataStartRowIndex = usableTable.GetProperty("dataStartRowIndex").GetInt32();
        var dataEndRowIndex = ReadNullableInt(usableTable, "dataEndRowIndex");

        headerRowIndex.Should().Be(7);
        headerRowCount.Should().Be(1);
        dataStartRowIndex.Should().Be(8);
        dataEndRowIndex.Should().Be(111);
        projectColumnIndex.Should().Be(2);
        specificationColumnIndex.Should().Be(3);
        acceptanceColumnIndex.Should().Be(8);
        remarkColumnIndex.Should().Be(9);
        usableTable.GetProperty("recommendation").GetString().Should().Be(
            "NeedConfirm",
            "多区域验收表需要确认范围，但前端仍应自动选中并进入确认页");
        var regions = usableTable.GetProperty("regions").EnumerateArray().ToList();
        regions.Should().HaveCount(2);
        ReadRegionCoordinates(regions[0]).Should().Be((7, 1, 8, 111));
        ReadRegionCoordinates(regions[1]).Should().Be(
            (125, 1, 126, 142),
            "第 126 行是第二段末级表头，第 127 行已经是业务数据，不能静默丢弃首行");
        regions[1].GetProperty("projectColumnIndex").GetInt32().Should().Be(2);
        regions[1].GetProperty("specificationColumnIndex").GetInt32().Should().Be(3);
        regions[1].GetProperty("acceptanceColumnIndex").GetInt32().Should().Be(8);
        regions[1].GetProperty("remarkColumnIndex").GetInt32().Should().Be(9);
        var remarkConflict = regions[0].GetProperty("fieldConflicts")
            .EnumerateArray()
            .Single(conflict => conflict.GetProperty("field").GetString() == "Remark");
        remarkConflict.GetProperty("recommendedColumnIndex").GetInt32().Should().Be(9);
        remarkConflict.GetProperty("candidates").EnumerateArray()
            .Select(candidate => (
                candidate.GetProperty("columnIndex").GetInt32(),
                candidate.GetProperty("header").GetString()))
            .Should().Equal((9, "Remark"), (14, "備註"));
        regions[1].GetProperty("headers")[2].GetString().Should().NotBe(headers[2], "第二段未录入的项目表头应独立参与学习");
        headers[projectColumnIndex!.Value].Should().Be("具體項目");
        headers[specificationColumnIndex!.Value].Should().Be("規格");
        headers[acceptanceColumnIndex!.Value].Should().Be("OK/NG");
        headers[remarkColumnIndex!.Value].Should().Be("Remark");

        var confirmResponse = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            customerId,
            fileId,
            tableIndex = 0,
            templateName = $"真实样本-{Path.GetFileNameWithoutExtension(samplePath)}",
            headers,
            projectColumnIndex,
            specificationColumnIndex,
            acceptanceColumnIndex,
            remarkColumnIndex,
            headerRowIndex,
            headerRowCount,
            dataStartRowIndex,
            dataEndRowIndex,
            isSpecificationOnly = projectColumnIndex == null,
            learnedColumns = BuildLearnedColumns(headers, projectColumnIndex, specificationColumnIndex, acceptanceColumnIndex, remarkColumnIndex),
            regions = regions.Select(region => new
            {
                regionId = region.GetProperty("regionId").GetString(),
                regionIndex = region.GetProperty("regionIndex").GetInt32(),
                headers = region.GetProperty("headers").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray(),
                projectColumnIndex = ReadNullableInt(region, "projectColumnIndex"),
                specificationColumnIndex = ReadNullableInt(region, "specificationColumnIndex"),
                acceptanceColumnIndex = ReadNullableInt(region, "acceptanceColumnIndex"),
                remarkColumnIndex = ReadNullableInt(region, "remarkColumnIndex"),
                headerRowIndex = region.GetProperty("headerRowIndex").GetInt32(),
                headerRowCount = region.GetProperty("headerRowCount").GetInt32(),
                dataStartRowIndex = region.GetProperty("dataStartRowIndex").GetInt32(),
                dataEndRowIndex = ReadNullableInt(region, "dataEndRowIndex"),
                isSpecificationOnly = region.GetProperty("isSpecificationOnly").GetBoolean()
            }).ToArray()
        }));
        var confirmText = await confirmResponse.Content.ReadAsStringAsync();
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK, confirmText);

        var confirmBody = await confirmResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        confirmBody.Code.Should().Be(0, confirmText);
        confirmBody.Data!.GetProperty("templateSaved").GetBoolean().Should().BeTrue();
        confirmBody.Data.GetProperty("learnedRuleCount").GetInt32().Should().BeGreaterThanOrEqualTo(4);

        var secondProjectHeader = regions[1].GetProperty("headers")[2].GetString();
        var learnedRulesResponse = await _client.GetAsync($"/api/column-mapping-rules/effective?customerId={customerId}");
        var learnedRulesBody = await learnedRulesResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        learnedRulesBody.Data.EnumerateArray().Should().Contain(item =>
            item.GetProperty("targetField").GetInt32() == 1 &&
            item.GetProperty("pattern").GetString() == secondProjectHeader &&
            item.GetProperty("customerId").GetInt32() == customerId);

        await CreateColumnRuleAsync(customerId, "裝機前驗機", targetField: 1);
        await CreateColumnRuleAsync(customerId, "裝機前驗機", targetField: 2);
        var reuseResponse = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));
        var reuseText = await reuseResponse.Content.ReadAsStringAsync();
        reuseResponse.StatusCode.Should().Be(HttpStatusCode.OK, reuseText);
        var reuseBody = await reuseResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var reusedTable = reuseBody.Data!.GetProperty("tables").EnumerateArray()
            .Single(table => table.GetProperty("tableIndex").GetInt32() == 0);
        reusedTable.GetProperty("source").GetString().Should().NotBe("Template",
            "存在未覆盖业务行时，历史模板不能覆盖当前文件重新识别出的范围");
        reusedTable.GetProperty("regions").EnumerateArray()
            .Select(ReadRegionCoordinates)
            .Should().Equal(regions.Select(ReadRegionCoordinates));

        Console.WriteLine("SMART_CONFIG_REAL_SAMPLE_SUMMARY " + JsonSerializer.Serialize(new
        {
            samplePath,
            fileId,
            tableCount = tables.Count,
            selectedTableIndex = usableTable.GetProperty("tableIndex").GetInt32(),
            tableName = usableTable.TryGetProperty("tableName", out var tableNameElement) ? tableNameElement.GetString() : null,
            decision = usableTable.GetProperty("decision").GetString(),
            source = usableTable.GetProperty("source").GetString(),
            confidence = usableTable.GetProperty("confidence").GetDouble(),
            headerRowIndex,
            headerRowCount,
            dataStartRowIndex,
            dataEndRowIndex,
            projectColumnIndex,
            specificationColumnIndex,
            acceptanceColumnIndex,
            remarkColumnIndex,
            regions = regions.Select(ReadRegionCoordinates).ToArray(),
            headers = headers.Take(12).ToArray()
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private async Task<int> CreateCustomerAsync(string name)
    {
        var response = await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name }));
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data!.GetProperty("id").GetInt32();
    }

    private async Task CreateColumnRuleAsync(int customerId, string pattern, int targetField)
    {
        var response = await _client.PostAsync(
            "/api/column-mapping-rules",
            ApiClientJson.ToJsonContent(new
            {
                pattern,
                targetField,
                matchMode = 2,
                priority = 200,
                enabled = true,
                source = 2,
                customerId
            }));
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
    }

    private async Task<int> UploadSampleAsync(string path)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(path));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", Path.GetFileName(path));

        var response = await _client.PostAsync("/api/documents/upload", content);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data!.GetProperty("fileId").GetInt32();
    }

    private static object[] BuildLearnedColumns(
        IReadOnlyList<string> headers,
        int? projectColumnIndex,
        int? specificationColumnIndex,
        int? acceptanceColumnIndex,
        int? remarkColumnIndex)
    {
        return new[]
            {
                BuildLearnedColumn(headers, projectColumnIndex, targetField: 1),
                BuildLearnedColumn(headers, specificationColumnIndex, targetField: 2),
                BuildLearnedColumn(headers, acceptanceColumnIndex, targetField: 3),
                BuildLearnedColumn(headers, remarkColumnIndex, targetField: 4)
            }
            .Where(item => item != null)
            .Cast<object>()
            .ToArray();
    }

    private static object? BuildLearnedColumn(IReadOnlyList<string> headers, int? columnIndex, int targetField)
    {
        if (!columnIndex.HasValue ||
            columnIndex.Value < 0 ||
            columnIndex.Value >= headers.Count ||
            string.IsNullOrWhiteSpace(headers[columnIndex.Value]))
        {
            return null;
        }

        return new
        {
            header = headers[columnIndex.Value],
            targetField
        };
    }

    private static (int HeaderRowIndex, int HeaderRowCount, int DataStartRowIndex, int DataEndRowIndex) ReadRegionCoordinates(JsonElement region)
    {
        return (
            region.GetProperty("headerRowIndex").GetInt32(),
            region.GetProperty("headerRowCount").GetInt32(),
            region.GetProperty("dataStartRowIndex").GetInt32(),
            region.GetProperty("dataEndRowIndex").GetInt32());
    }
    private static int? ReadNullableInt(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();
    }
}
