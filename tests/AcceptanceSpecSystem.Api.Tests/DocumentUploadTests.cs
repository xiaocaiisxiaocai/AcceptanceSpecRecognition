using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class DocumentUploadTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly ApiWebApplicationFactory _factory;

    public DocumentUploadTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
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

    [Fact]
    public async Task Upload_ShouldReturnPersistedContentHash()
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

        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();
        var responseHash = uploadJson.Data.GetProperty("fileHash").GetString();
        responseHash.Should().NotBeNullOrWhiteSpace();
        responseHash!.Length.Should().Be(64);
        responseHash.Should().Be(FileStorageService.ComputeSha256(bytes));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedHash = await dbContext.WordFiles
            .Where(file => file.Id == fileId)
            .Select(file => file.FileHash)
            .FirstAsync();

        persistedHash.Should().Be(responseHash);
    }

    [Fact]
    public async Task Upload_ShouldDeferTableCountUntilTablesEndpointIsRequested()
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

        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();
        uploadJson.Data.GetProperty("tableCount").GetInt32().Should().Be(0);
        uploadJson.Data.GetProperty("tableCountReady").GetBoolean().Should().BeFalse();

        var tablesResp = await _client.GetAsync($"/api/documents/{fileId}/tables");
        tablesResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var tablesJson = await tablesResp.ReadAsAsync<ApiResponse<JsonElement>>();
        tablesJson.Code.Should().Be(0);
        tablesJson.Data.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetFiles_WhenCurrentUserOutOfScope_ShouldExcludeFile()
    {
        var (inScopeFileId, outOfScopeFileId) = await SeedScopedFilesAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/documents");
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-Permissions", "*:*:*");

        using var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var items = json.Data.GetProperty("items").EnumerateArray().ToList();

        items.Should().Contain(item => item.GetProperty("id").GetInt32() == inScopeFileId);
        items.Should().NotContain(item => item.GetProperty("id").GetInt32() == outOfScopeFileId);
    }

    [Fact]
    public async Task GetTables_WhenCurrentUserOutOfScope_ShouldReturnNotFound()
    {
        var (_, outOfScopeFileId) = await SeedScopedFilesAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/{outOfScopeFileId}/tables");
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-Permissions", "*:*:*");

        using var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<(int InScopeFileId, int OutOfScopeFileId)> SeedScopedFilesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var inScopeOrg = new OrgUnit
        {
            CompanyId = 1,
            ParentId = 1,
            UnitType = OrgUnitType.Division,
            Code = $"DOC-IN-{Guid.NewGuid():N}"[..12],
            Name = "文档范围内组织",
            Path = "/1/",
            Depth = 1,
            Sort = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var outOfScopeOrg = new OrgUnit
        {
            CompanyId = 1,
            ParentId = 1,
            UnitType = OrgUnitType.Division,
            Code = $"DOC-OUT-{Guid.NewGuid():N}"[..12],
            Name = "文档范围外组织",
            Path = "/1/",
            Depth = 1,
            Sort = 2,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.OrgUnits.AddRange(inScopeOrg, outOfScopeOrg);
        await dbContext.SaveChangesAsync();

        inScopeOrg.Path = $"/1/{inScopeOrg.Id}/";
        outOfScopeOrg.Path = $"/1/{outOfScopeOrg.Id}/";

        var inScopeFile = new WordFile
        {
            CompanyId = 1,
            CreatedByUserId = 1,
            OwnerOrgUnitId = inScopeOrg.Id,
            FileName = $"in-scope-{Guid.NewGuid():N}.docx",
            FileType = UploadedFileType.WordDocx,
            FileContent = Array.Empty<byte>(),
            FileHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            UploadedAt = DateTime.UtcNow
        };
        var outOfScopeFile = new WordFile
        {
            CompanyId = 1,
            CreatedByUserId = 1,
            OwnerOrgUnitId = outOfScopeOrg.Id,
            FileName = $"out-of-scope-{Guid.NewGuid():N}.docx",
            FileType = UploadedFileType.WordDocx,
            FileContent = Array.Empty<byte>(),
            FileHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            UploadedAt = DateTime.UtcNow
        };

        dbContext.WordFiles.AddRange(inScopeFile, outOfScopeFile);
        await dbContext.SaveChangesAsync();

        await ConfigureCommonSpecScopeAsync(dbContext, inScopeOrg.Id);
        return (inScopeFile.Id, outOfScopeFile.Id);
    }

    private static async Task ConfigureCommonSpecScopeAsync(AppDbContext dbContext, int orgUnitId)
    {
        var commonRoleId = await dbContext.AuthRoles
            .Where(role => role.Code == "common")
            .Select(role => role.Id)
            .FirstAsync();
        var roleScopes = await dbContext.AuthRoleDataScopes
            .Include(scope => scope.Nodes)
            .Where(scope => scope.RoleId == commonRoleId && scope.Resource == "spec")
            .OrderBy(scope => scope.Id)
            .ToListAsync();
        var roleScope = roleScopes.FirstOrDefault();

        if (roleScope == null)
        {
            roleScope = new AuthRoleDataScope
            {
                RoleId = commonRoleId,
                Resource = "spec",
                ScopeType = DataScopeType.CustomNodes,
                CreatedAt = DateTime.UtcNow
            };
            dbContext.AuthRoleDataScopes.Add(roleScope);
        }
        else
        {
            roleScope.ScopeType = DataScopeType.CustomNodes;
            dbContext.AuthRoleDataScopeNodes.RemoveRange(roleScope.Nodes);
            roleScope.Nodes.Clear();

            if (roleScopes.Count > 1)
            {
                dbContext.AuthRoleDataScopes.RemoveRange(roleScopes.Skip(1));
            }
        }

        roleScope.Nodes.Add(new AuthRoleDataScopeNode
        {
            OrgUnitId = orgUnitId
        });

        await dbContext.SaveChangesAsync();
    }
}

