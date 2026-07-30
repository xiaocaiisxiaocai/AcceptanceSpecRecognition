using System.Net;
using System.Text.Json;
using System.Net.Http.Headers;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class SmartConfigFileDataScopeTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SmartConfigFileDataScopeTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenFileBelongsToAnotherUserOutsideScope_ShouldReturnNotFound()
    {
        var fileId = await UploadAsAdminAsync();
        await RestrictCommonRoleToSelfAsync();

        using var request = CreateCommonRequest(
            "/api/smart-config/recognize",
            new { fileId, customerId = (int?)null });
        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Recognize_WhenOwnedFileUsesCustomerOutsideSpecScope_ShouldReturnNotFound()
    {
        await RestrictCommonRoleToSelfAsync();
        var fileId = await UploadAsCommonAsync();
        var customerId = await CreateCustomerAsync();
        await SeedOutOfScopeSpecAsync(customerId, fileId);

        using var request = CreateCommonRequest(
            "/api/smart-config/recognize",
            new { fileId, customerId });
        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Confirm_WhenModifiedStructureReferencesFileOutsideScope_ShouldNotSaveTemplate()
    {
        var fileId = await UploadAsAdminAsync();
        var customerId = await CreateCustomerAsync();
        await RestrictCommonRoleToSelfAsync();

        using var request = CreateCommonRequest(
            "/api/smart-config/confirm",
            new
            {
                fileId,
                tableIndex = 0,
                customerId,
                templateName = "越权模板",
                headers = new[] { "项目", "规格" },
                projectColumnIndex = 0,
                specificationColumnIndex = 1,
                headerRowIndex = 0,
                headerRowCount = 1,
                dataStartRowIndex = 1,
                userModifiedStructure = true,
                learnedColumns = Array.Empty<object>()
            });
        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.DocumentTemplates.AnyAsync(item => item.CustomerId == customerId)).Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_WhenFileIdIsMissing_ShouldNotSaveTemplateOrLearningRules()
    {
        var customerId = await CreateCustomerAsync();

        using var response = await _client.PostAsync(
            "/api/smart-config/confirm",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                templateName = "无文件模板",
                headers = new[] { "项目", "规格" },
                projectColumnIndex = 0,
                specificationColumnIndex = 1,
                headerRowIndex = 0,
                headerRowCount = 1,
                dataStartRowIndex = 1,
                learnedColumns = new[]
                {
                    new { header = "项目", targetField = 1 },
                    new { header = "规格", targetField = 2 }
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("确认结构时必须提供有效FileId");
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.DocumentTemplates.AnyAsync(item => item.CustomerId == customerId)).Should().BeFalse();
        (await db.ColumnMappingRules.AnyAsync(item => item.CustomerId == customerId)).Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_WhenRequestHeadersAreForged_ShouldPersistActualFileHeadersOnly()
    {
        var fileId = await UploadAsAdminAsync();
        var customerId = await CreateCustomerAsync();

        using var response = await _client.PostAsync(
            "/api/smart-config/confirm",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                tableIndex = 0,
                customerId,
                templateName = "伪造表头测试",
                headers = new[] { "伪造项目", "伪造规格", "伪造验收", "伪造备注" },
                projectColumnIndex = 0,
                specificationColumnIndex = 1,
                acceptanceColumnIndex = 2,
                remarkColumnIndex = 3,
                headerRowIndex = 0,
                headerRowCount = 1,
                dataStartRowIndex = 1,
                learnedColumns = new[]
                {
                    new { header = "伪造项目", targetField = 1 },
                    new { header = "伪造规格", targetField = 2 },
                    new { header = "伪造验收", targetField = 3 },
                    new { header = "伪造备注", targetField = 4 }
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var template = await db.DocumentTemplates.SingleAsync(item => item.CustomerId == customerId);
        JsonSerializer.Deserialize<string[]>(template.HeadersJson)
            .Should().Equal("项目", "规格", "验收", "备注");
        var patterns = await db.ColumnMappingRules
            .Where(item => item.CustomerId == customerId)
            .Select(item => item.Pattern)
            .ToListAsync();
        patterns.Should().Contain(["项目", "规格", "验收", "备注"])
            .And.NotContain(item => item.Contains("伪造"));
    }

    [Fact]
    public async Task Confirm_WhenUnmodifiedCoordinatesExceedActualTable_ShouldReject()
    {
        var fileId = await UploadAsAdminAsync();
        var customerId = await CreateCustomerAsync();

        using var response = await _client.PostAsync(
            "/api/smart-config/confirm",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                tableIndex = 0,
                customerId,
                headers = new[] { "项目", "规格" },
                projectColumnIndex = 0,
                specificationColumnIndex = 1,
                headerRowIndex = 2,
                headerRowCount = 1,
                dataStartRowIndex = 3,
                learnedColumns = Array.Empty<object>()
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Confirm_WhenCommonUserOwnsFileButCustomerIsOutsideSpecScope_ShouldRejectWithoutWrites()
    {
        await RestrictCommonRoleToSelfAsync();
        var fileId = await UploadAsCommonAsync();
        var customerId = await CreateCustomerAsync();
        await SeedOutOfScopeSpecAsync(customerId, fileId);

        using var request = CreateCommonRequest(
            "/api/smart-config/confirm",
            new
            {
                fileId,
                tableIndex = 0,
                customerId,
                headers = new[] { "项目", "规格" },
                projectColumnIndex = 0,
                specificationColumnIndex = 1,
                headerRowIndex = 0,
                headerRowCount = 1,
                dataStartRowIndex = 1,
                learnedColumns = Array.Empty<object>()
            });
        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.DocumentTemplates.AnyAsync(item => item.CustomerId == customerId)).Should().BeFalse();
        (await db.ColumnMappingRules.AnyAsync(item => item.CustomerId == customerId)).Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_WhenNonAllScopeTargetsEmptyCustomer_ShouldFailClosed()
    {
        await RestrictCommonRoleToSelfAsync();
        var fileId = await UploadAsCommonAsync();
        var customerId = await CreateCustomerAsync();

        using var request = CreateCommonRequest(
            "/api/smart-config/confirm",
            new
            {
                fileId,
                tableIndex = 0,
                customerId,
                headers = new[] { "项目", "规格" },
                projectColumnIndex = 0,
                specificationColumnIndex = 1,
                headerRowIndex = 0,
                headerRowCount = 1,
                dataStartRowIndex = 1,
                learnedColumns = Array.Empty<object>()
            });
        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<int> UploadAsAdminAsync()
    {
        return await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateExcelBytes(),
            $"smart-config-scope-{Guid.NewGuid():N}.xlsx");
    }

    private async Task<int> UploadAsCommonAsync()
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(CreateExcelBytes());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", $"smart-config-common-{Guid.NewGuid():N}.xlsx");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/documents/upload")
        {
            Content = content
        };
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-Permissions", "*:*:*");
        using var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return body.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "验收";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "OK";
        worksheet.Cell(2, 4).Value = "抽检";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private async Task<int> CreateCustomerAsync()
    {
        using var response = await _client.PostAsync(
            "/api/customers",
            ApiClientJson.ToJsonContent(new { name = $"数据范围客户-{Guid.NewGuid():N}" }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return body.Data.GetProperty("id").GetInt32();
    }

    private async Task RestrictCommonRoleToSelfAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = await db.AuthRoles
            .Include(item => item.DataScopes)
                .ThenInclude(item => item.Nodes)
            .SingleAsync(item => item.Code == "common");
        var specScopes = role.DataScopes.Where(item => item.Resource == "spec").ToList();
        var specScope = specScopes.First();
        specScope.ScopeType = DataScopeType.Self;
        db.AuthRoleDataScopeNodes.RemoveRange(specScope.Nodes);
        if (specScopes.Count > 1)
        {
            db.AuthRoleDataScopes.RemoveRange(specScopes.Skip(1));
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedOutOfScopeSpecAsync(int customerId, int fileId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AcceptanceSpecs.Add(new AcceptanceSpec
        {
            CustomerId = customerId,
            Project = "范围外项目",
            Specification = "范围外规格",
            WordFileId = fileId,
            CreatedByUserId = 1,
            OwnerOrgUnitId = 1,
            ImportedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static HttpRequestMessage CreateCommonRequest(string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = ApiClientJson.ToJsonContent(body)
        };
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-Permissions", "*:*:*");
        return request;
    }
}
