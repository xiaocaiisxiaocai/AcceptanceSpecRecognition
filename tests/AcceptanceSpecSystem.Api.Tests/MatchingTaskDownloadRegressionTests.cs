using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class MatchingTaskDownloadRegressionTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public MatchingTaskDownloadRegressionTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Download_ForWordTask_ShouldBeRepeatable_AndShouldNotDeleteOriginalSourceFile()
    {
        var originalBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" }
        });

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(originalBytes), "file", "matching-download-repeatable.docx");
        var uploadResp = await _client.PostAsync("/api/documents/upload", multipart);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();

        var customerResp = await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name = "Repeatable-Customer" }));
        var customerId = (await customerResp.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processResp = await _client.PostAsync("/api/processes", ApiClientJson.ToJsonContent(new { name = "Repeatable-Process" }));
        var processId = (await processResp.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var specResp = await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "P1",
            specification = "S1",
            acceptance = "DB-AC-1",
            remark = "DB-REM-1"
        }));
        var specId = (await specResp.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var executeResp = await _client.PostAsync("/api/matching/execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                tableIndex = 0,
                acceptanceColumnIndex = 2,
                remarkColumnIndex = 3,
                highConfidenceThreshold = 0.85,
                mappings = new[]
                {
                    new
                    {
                        rowIndex = 1,
                        specId,
                        matchScore = 0.9
                    }
                }
            }));

        executeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var executeJson = await executeResp.ReadAsAsync<ApiResponse<JsonElement>>();
        executeJson.Code.Should().Be(0);
        var taskId = executeJson.Data.GetProperty("taskId").GetString();
        taskId.Should().NotBeNullOrWhiteSpace();

        string sourceRelativePath;
        byte[] sourceBeforeDownload;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            var sourceFile = await dbContext.WordFiles.SingleAsync(file => file.Id == fileId);

            sourceFile.FilePath.Should().NotBeNullOrWhiteSpace();
            sourceRelativePath = sourceFile.FilePath!;
            sourceBeforeDownload = File.ReadAllBytes(fileStorage.GetAbsolutePath(sourceRelativePath));
        }

        GetCellText(sourceBeforeDownload, 0, 1, 2).Should().BeEmpty();
        GetCellText(sourceBeforeDownload, 0, 1, 3).Should().BeEmpty();

        var firstDownloadResp = await _client.GetAsync($"/api/matching/download/{taskId}");
        firstDownloadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBytes = await firstDownloadResp.Content.ReadAsByteArrayAsync();

        var secondDownloadResp = await _client.GetAsync($"/api/matching/download/{taskId}");
        secondDownloadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBytes = await secondDownloadResp.Content.ReadAsByteArrayAsync();

        firstBytes.Should().Equal(secondBytes, "重复下载应稳定返回同一份结果文档");
        GetCellText(firstBytes, 0, 1, 2).Should().Be("DB-AC-1");
        GetCellText(firstBytes, 0, 1, 3).Should().Be("DB-REM-1");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            var sourceFile = await dbContext.WordFiles.SingleAsync(file => file.Id == fileId);

            sourceFile.FilePath.Should().Be(sourceRelativePath, "下载结果不应破坏原始上传文件的路径记录");
            var sourceAfterDownload = File.ReadAllBytes(fileStorage.GetAbsolutePath(sourceRelativePath));
            GetCellText(sourceAfterDownload, 0, 1, 2).Should().BeEmpty("Word 源文件应保持不变");
            GetCellText(sourceAfterDownload, 0, 1, 3).Should().BeEmpty("Word 源文件应保持不变");
        }
    }

    [Fact]
    public async Task Download_WhenPersistedArtifactFileHasBeenDeleted_ShouldReturn404()
    {
        var originalBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" }
        });

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(originalBytes), "file", "matching-download-missing-artifact.docx");
        var uploadResp = await _client.PostAsync("/api/documents/upload", multipart);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();

        var customerResp = await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name = "MissingArtifact-Customer" }));
        var customerId = (await customerResp.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processResp = await _client.PostAsync("/api/processes", ApiClientJson.ToJsonContent(new { name = "MissingArtifact-Process" }));
        var processId = (await processResp.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var specResp = await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "P1",
            specification = "S1",
            acceptance = "DB-AC-1",
            remark = "DB-REM-1"
        }));
        var specId = (await specResp.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var executeResp = await _client.PostAsync("/api/matching/execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                tableIndex = 0,
                acceptanceColumnIndex = 2,
                remarkColumnIndex = 3,
                highConfidenceThreshold = 0.85,
                mappings = new[]
                {
                    new
                    {
                        rowIndex = 1,
                        specId,
                        matchScore = 0.9
                    }
                }
            }));

        executeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var executeJson = await executeResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var taskId = executeJson.Data.GetProperty("taskId").GetString();
        taskId.Should().NotBeNullOrWhiteSpace();

        var firstDownloadResp = await _client.GetAsync($"/api/matching/download/{taskId}");
        firstDownloadResp.StatusCode.Should().Be(HttpStatusCode.OK);

        string artifactPath;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            var snapshot = await dbContext.MatchingFillTasks.SingleAsync(item => item.TaskId == taskId);
            using var payload = JsonDocument.Parse(snapshot.PayloadJson);
            var artifactRelativePath = payload.RootElement.GetProperty("downloadArtifactRelativePath").GetString();
            artifactRelativePath.Should().NotBeNullOrWhiteSpace();
            artifactPath = fileStorage.GetAbsolutePath(artifactRelativePath!);
        }

        File.Delete(artifactPath);

        var downloadResp = await _client.GetAsync($"/api/matching/download/{taskId}");
        downloadResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var json = await downloadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(404);
    }

    private static byte[] CreateDocxBytes(string[][] rows)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            var table = new Table();
            table.AppendChild(new TableProperties(new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

            foreach (var row in rows)
            {
                var tableRow = new TableRow();
                foreach (var cell in row)
                {
                    tableRow.AppendChild(new TableCell(new Paragraph(new Run(new Text(cell ?? string.Empty)))));
                }

                table.AppendChild(tableRow);
            }

            mainPart.Document.Body!.Append(table);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static string GetCellText(byte[] docxBytes, int tableIndex, int rowIndex, int columnIndex)
    {
        using var stream = new MemoryStream(docxBytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var table = document.MainDocumentPart!.Document!.Body!.Descendants<Table>().ToList()[tableIndex];
        var row = table.Elements<TableRow>().ToList()[rowIndex];
        var cell = row.Elements<TableCell>().ToList()[columnIndex];
        return cell.InnerText ?? string.Empty;
    }
}
