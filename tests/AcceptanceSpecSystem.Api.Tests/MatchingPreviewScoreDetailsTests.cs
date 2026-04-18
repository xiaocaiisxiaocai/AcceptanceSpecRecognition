using System.Net;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;

namespace AcceptanceSpecSystem.Api.Tests;

public class MatchingPreviewScoreDetailsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public MatchingPreviewScoreDetailsTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Preview_ShouldIncludeEmbeddingScoreDetails()
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = "ScoreDetails-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = "ScoreDetails-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var specResp = await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = "P1",
                specification = "S1",
                acceptance = "OK-1",
                remark = "R1"
            }));
        specResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var fileId = await UploadSingleRowDocxAsync("P1", "S1");
        var previewResp = await _client.PostAsync(
            "/api/matching/batch-preview",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                customerId,
                processId,
                config = new { minScoreThreshold = 0.0 },
                tables = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        projectColumnIndex = 0,
                        specificationColumnIndex = 1,
                        acceptanceColumnIndex = 2,
                        remarkColumnIndex = 3
                    }
                }
            }));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);
        previewJson.Data.ValueKind.Should().NotBe(JsonValueKind.Undefined);

        var item = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0];
        item.TryGetProperty("bestMatch", out var bestMatch).Should().BeTrue();
        bestMatch.ValueKind.Should().NotBe(JsonValueKind.Null);

        var scoreDetails = bestMatch.GetProperty("scoreDetails");
        scoreDetails.TryGetProperty("Embedding", out _).Should().BeTrue();
        scoreDetails.TryGetProperty("Levenshtein", out _).Should().BeFalse();
        scoreDetails.TryGetProperty("Jaccard", out _).Should().BeFalse();
        scoreDetails.TryGetProperty("Cosine", out _).Should().BeFalse();

        var topCandidates = bestMatch.GetProperty("topCandidates");
        topCandidates.GetArrayLength().Should().BeGreaterThan(0);
        topCandidates[0].GetProperty("rank").GetInt32().Should().Be(1);
        topCandidates[0].GetProperty("scoreDetails").TryGetProperty("Embedding", out _).Should().BeTrue();
    }

    private async Task<int> UploadSingleRowDocxAsync(string project, string specification)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { project, specification, "", "" }
        })), "file", "score-details-preview.docx");

        var response = await _client.PostAsync("/api/documents/upload", multipart);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateDocxBytes(string[][] rows)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
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
}
