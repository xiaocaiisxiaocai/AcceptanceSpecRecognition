using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

internal static class SmartConfigRecognizeTestFiles
{
    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string WordContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public static Task<int> UploadExcelAsync(HttpClient client, byte[] bytes, string fileName) =>
        UploadAsync(client, bytes, fileName, ExcelContentType);

    public static Task<int> UploadWordAsync(HttpClient client, byte[] bytes, string fileName) =>
        UploadAsync(client, bytes, fileName, WordContentType);

    public static byte[] CreateWordBytes(string[][] rows)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body());

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
                    tableRow.AppendChild(new TableCell(
                        new Paragraph(new Run(new Text(cell ?? string.Empty)))));
                }

                table.AppendChild(tableRow);
            }

            main.Document.Body!.Append(table);
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static async Task<int> UploadAsync(
        HttpClient client,
        byte[] bytes,
        string fileName,
        string contentType)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);

        using var contextResponse = await client.GetAsync("/api/org-units/business-context");
        var contextText = await contextResponse.Content.ReadAsStringAsync();
        contextResponse.StatusCode.Should().Be(HttpStatusCode.OK, contextText);
        var context = JsonSerializer.Deserialize<ApiResponse<JsonElement>>(
            contextText,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var contextData = context.Data;
        var businessOrgUnitId = contextData.TryGetProperty("currentOrgUnitId", out var currentOrgUnitId) &&
                                currentOrgUnitId.ValueKind == JsonValueKind.Number
            ? currentOrgUnitId.GetInt32()
            : contextData.GetProperty("options").EnumerateArray().First().GetProperty("id").GetInt32();
        content.Add(new StringContent(businessOrgUnitId.ToString()), "businessOrgUnitId");

        var response = await client.PostAsync("/api/documents/upload", content);
        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);

        var json = JsonSerializer.Deserialize<ApiResponse<JsonElement>>(
            responseText,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return json.Data.GetProperty("fileId").GetInt32();
    }
}
