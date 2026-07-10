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
        dataEndRowIndex.Should().Be(194);
        projectColumnIndex.Should().Be(2);
        specificationColumnIndex.Should().Be(3);
        acceptanceColumnIndex.Should().Be(8);
        remarkColumnIndex.Should().Be(9);
        headers[projectColumnIndex!.Value].Should().Be("具體項目");
        headers[specificationColumnIndex!.Value].Should().Be("規格");
        headers[acceptanceColumnIndex!.Value].Should().Be("OK/NG");
        headers[remarkColumnIndex!.Value].Should().Be("Remark");

        var confirmResponse = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            customerId,
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
            learnedColumns = BuildLearnedColumns(headers, projectColumnIndex, specificationColumnIndex, acceptanceColumnIndex, remarkColumnIndex)
        }));
        var confirmText = await confirmResponse.Content.ReadAsStringAsync();
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK, confirmText);

        var confirmBody = await confirmResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        confirmBody.Code.Should().Be(0, confirmText);
        confirmBody.Data!.GetProperty("templateSaved").GetBoolean().Should().BeTrue();

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

    private static int? ReadNullableInt(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();
    }
}
