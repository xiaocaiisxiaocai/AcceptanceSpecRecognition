using System.Net;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;

namespace AcceptanceSpecSystem.Api.Tests;

public class E2EFillFlowTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public E2EFillFlowTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Upload_Preview_Execute_Download_ShouldFillAcceptanceColumn()
    {
        // 1) Create a tiny docx with a single table (Project / Spec / Acceptance / Remark)
        var originalDoc = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" },
            new[] { "P2", "S2", "", "" }
        });

        // 2) Upload document
        var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(originalDoc), "file", "e2e.docx");
        var uploadResp = await _client.PostAsync("/api/documents/upload", multipart);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        uploadJson.Code.Should().Be(0);
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();

        // 3) Seed specs that should match
        var customerId = (await (await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name = "E2E-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync("/api/processes", ApiClientJson.ToJsonContent(new { name = "E2E-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var spec1 = await (await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new { customerId, processId, project = "P1", specification = "S1", acceptance = "OK-1", remark = "R1" })))
            .ReadAsAsync<ApiResponse<JsonElement>>();
        spec1.Code.Should().Be(0);

        var spec2 = await (await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new { customerId, processId, project = "P2", specification = "S2", acceptance = "OK-2", remark = "R2" })))
            .ReadAsAsync<ApiResponse<JsonElement>>();
        spec2.Code.Should().Be(0);

        // 4) Preview (file mode, manual column indices)
        var previewResp = await _client.PostAsync(
            "/api/matching/preview",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                tableIndex = 0,
                projectColumnIndex = 0,
                specificationColumnIndex = 1,
                customerId,
                processId,
                config = new { useLevenshtein = true, useJaccard = true, useCosine = true, minScoreThreshold = 0.0, maxCandidates = 5 }
            }));
        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);

        var items = previewJson.Data.GetProperty("items");
        items.GetArrayLength().Should().Be(2);

        // Build mappings using bestMatch
        var mappings = items.EnumerateArray().Select(i => new
        {
            rowIndex = i.GetProperty("rowIndex").GetInt32(),
            specId = i.GetProperty("bestMatch").GetProperty("specId").GetInt32()
        }).ToArray();

        // 5) Execute fill (manual acceptance/remark columns)
        var execResp = await _client.PostAsync(
            "/api/matching/execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                tableIndex = 0,
                acceptanceColumnIndex = 2,
                remarkColumnIndex = 3,
                mappings
            }));
        execResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(0);
        var taskId = execJson.Data.GetProperty("taskId").GetString();
        taskId.Should().NotBeNullOrWhiteSpace();

        // 6) Download and validate content is filled
        var dlResp = await _client.GetAsync($"/api/matching/download/{taskId}");
        dlResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var filledBytes = await dlResp.Content.ReadAsByteArrayAsync();
        filledBytes.Length.Should().BeGreaterThan(0);

        GetCellText(filledBytes, tableIndex: 0, rowIndex: 1, colIndex: 2).Should().Be("OK-1");
        GetCellText(filledBytes, tableIndex: 0, rowIndex: 1, colIndex: 3).Should().Be("R1");
        GetCellText(filledBytes, tableIndex: 0, rowIndex: 2, colIndex: 2).Should().Be("OK-2");
        GetCellText(filledBytes, tableIndex: 0, rowIndex: 2, colIndex: 3).Should().Be("R2");
    }

    private static byte[] CreateDocxBytes(string[][] rows)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());

            var table = new Table();
            table.AppendChild(new TableProperties(new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }
            )));

            for (var r = 0; r < rows.Length; r++)
            {
                var tr = new TableRow();
                for (var c = 0; c < rows[r].Length; c++)
                {
                    tr.AppendChild(new TableCell(new Paragraph(new Run(new Text(rows[r][c] ?? string.Empty)))
                    {
                        ParagraphProperties = new ParagraphProperties()
                    }));
                }
                table.AppendChild(tr);
            }

            var body = main.Document.Body ?? new Body();
            if (main.Document.Body == null)
                main.Document.Body = body;
            body.Append(table);
            main.Document.Save();
        }

        return ms.ToArray();
    }

    private static string GetCellText(byte[] docx, int tableIndex, int rowIndex, int colIndex)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        var mainPart = doc.MainDocumentPart
                       ?? throw new InvalidOperationException("MainDocumentPart not found");
        var body = mainPart.Document?.Body
                   ?? throw new InvalidOperationException("Document body not found");
        var tables = body.Descendants<Table>().ToList();
        tables.Count.Should().BeGreaterThan(tableIndex);
        var table = tables[tableIndex];

        var rows = table.Elements<TableRow>().ToList();
        rows.Count.Should().BeGreaterThan(rowIndex);
        var row = rows[rowIndex];

        var cells = row.Elements<TableCell>().ToList();
        cells.Count.Should().BeGreaterThan(colIndex);
        return cells[colIndex].InnerText ?? string.Empty;
    }
}

