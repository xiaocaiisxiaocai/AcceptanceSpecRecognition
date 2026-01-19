using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;

namespace AcceptanceSpecSystem.Api.Tests;

public class DocumentUploadTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DocumentUploadTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Upload_And_GetTables_ShouldWork()
    {
        var docPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "example.docx"));
        File.Exists(docPath).Should().BeTrue($"Missing test file: {docPath}");

        var bytes = await File.ReadAllBytesAsync(docPath);
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        content.Add(fileContent, "file", "example.docx");

        var uploadResp = await _client.PostAsync("/api/documents/upload", content);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        uploadJson.Code.Should().Be(0);
        uploadJson.Data.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();
        fileId.Should().BeGreaterThan(0);

        var tablesResp = await _client.GetAsync($"/api/documents/{fileId}/tables");
        tablesResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var tablesJson = await tablesResp.ReadAsAsync<ApiResponse<JsonElement>>();
        tablesJson.Code.Should().Be(0);
        tablesJson.Data.ValueKind.Should().Be(JsonValueKind.Array);
    }
}

