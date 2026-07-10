using System.Net;
using System.Reflection;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class SmartConfigDataEndRowCoordinateTests : IClassFixture<ApiWebApplicationFactory>
{
    private const int TotalRowCount = 195;
    private readonly HttpClient _client;

    public SmartConfigDataEndRowCoordinateTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenExcelHeaderStartsAtRowEight_ShouldReturnOriginalTableEndCoordinate()
    {
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateExcelBytes(),
            "smart-recognize-excel-end-coordinate.xlsx");

        var table = await RecognizeSingleTableAsync(fileId);

        AssertOriginalRowCoordinates(table);
    }

    [Fact]
    public async Task Recognize_WhenWordHeaderStartsAtRowEight_ShouldReturnOriginalTableEndCoordinate()
    {
        var fileId = await SmartConfigRecognizeTestFiles.UploadWordAsync(
            _client,
            SmartConfigRecognizeTestFiles.CreateWordBytes(CreateRows()),
            "smart-recognize-word-end-coordinate.docx");

        var table = await RecognizeSingleTableAsync(fileId);

        AssertOriginalRowCoordinates(table);
    }

    [Fact]
    public void FromMapping_WhenReextractedRowCountIsLocal_ShouldUseOriginalTableEndCoordinate()
    {
        var recognized = InvokeFactory(
            "FromMapping",
            CreateTableInfo(),
            CreateReextractedTableData(),
            CreateMappingResult(),
            CreateHealthCheckResult(),
            false);

        recognized.DataEndRowIndex.Should().Be(TotalRowCount - 1);
    }

    [Fact]
    public void FromCandidate_WhenEndCoordinateIsMissing_ShouldUseOriginalTableEndCoordinate()
    {
        var recognized = InvokeFactory(
            "FromCandidate",
            CreateTableInfo(),
            CreateReextractedTableData(),
            new DocumentStructureCandidate
            {
                HeaderRowIndex = 7,
                HeaderRowCount = 1,
                DataStartRowIndex = 8,
                ProjectColumnIndex = 0,
                SpecificationColumnIndex = 1,
                AcceptanceColumnIndex = 2,
                RemarkColumnIndex = 3,
                Confidence = 0.9
            },
            CreateHealthCheckResult());

        recognized.DataEndRowIndex.Should().Be(TotalRowCount - 1);
    }

    [Fact]
    public void FromTemplate_WhenConfiguredEndUsesOriginalCoordinate_ShouldNotClampToLocalRowCount()
    {
        var recognized = InvokeFactory(
            "FromTemplate",
            CreateTableInfo(),
            CreateReextractedTableData(),
            new DocumentTemplate
            {
                HeaderRowIndex = 7,
                HeaderRowCount = 1,
                DataStartRowIndex = 8,
                DataEndRowIndex = TotalRowCount - 1,
                ProjectColumnIndex = 0,
                SpecificationColumnIndex = 1,
                AcceptanceColumnIndex = 2,
                RemarkColumnIndex = 3
            },
            CreateHeaders(),
            CreateHealthCheckResult());

        recognized.DataEndRowIndex.Should().Be(TotalRowCount - 1);
    }

    private async Task<JsonElement> RecognizeSingleTableAsync(int fileId)
    {
        var response = await _client.PostAsync(
            "/api/smart-config/recognize",
            ApiClientJson.ToJsonContent(new { fileId }));
        var responseText = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return body.Data.GetProperty("tables").EnumerateArray().Single();
    }

    private static void AssertOriginalRowCoordinates(JsonElement table)
    {
        table.GetProperty("headerRowIndex").GetInt32().Should().Be(7);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(1);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(8);
        table.GetProperty("dataEndRowIndex").GetInt32().Should().Be(TotalRowCount - 1);
    }

    private static SmartConfigurationRecognizedTable InvokeFactory(string methodName, params object[] arguments)
    {
        var factoryType = typeof(SmartConfigurationAppService).Assembly.GetType(
            "AcceptanceSpecSystem.Application.Services.SmartConfigurationRecognizedTableFactory");
        factoryType.Should().NotBeNull();
        var method = factoryType!.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(candidate =>
                candidate.Name == methodName &&
                candidate.GetParameters().Length == arguments.Length);

        return (SmartConfigurationRecognizedTable)method.Invoke(null, arguments)!;
    }

    private static TableInfo CreateTableInfo() => new()
    {
        Index = 0,
        Name = "验收表",
        RowCount = TotalRowCount,
        ColumnCount = 4
    };

    private static TableData CreateReextractedTableData() => new()
    {
        TableIndex = 0,
        Headers = CreateHeaders(),
        TotalDataRowCount = TotalRowCount - 8
    };

    private static ColumnMappingResult CreateMappingResult() => new()
    {
        Confidence = 0.9,
        Mapping = new ColumnMapping
        {
            HeaderRowIndex = 7,
            HeaderRowCount = 1,
            DataStartRowIndex = 8,
            ProjectColumn = 0,
            SpecificationColumn = 1,
            AcceptanceColumn = 2,
            RemarkColumn = 3
        }
    };

    private static DocumentStructureHealthCheckResult CreateHealthCheckResult() => new()
    {
        Decision = DocumentStructureDecision.AutoApply
    };

    private static List<string> CreateHeaders() => ["项目", "规格", "验收结果", "备注"];

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        var rows = CreateRows();
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            {
                worksheet.Cell(rowIndex + 1, columnIndex + 1).Value = rows[rowIndex][columnIndex];
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string[][] CreateRows()
    {
        var rows = new string[TotalRowCount][];
        for (var rowIndex = 0; rowIndex < 7; rowIndex++)
        {
            rows[rowIndex] = [$"文件说明{rowIndex + 1}", string.Empty, string.Empty, string.Empty];
        }

        rows[7] = ["项目", "规格", "验收结果", "备注"];
        for (var rowIndex = 8; rowIndex < rows.Length; rowIndex++)
        {
            rows[rowIndex] = [$"外观{rowIndex - 7}", "不得有明显划伤", "OK", "抽检"];
        }

        return rows;
    }
}
