using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class BusinessOrgOperationIsolationTests
{
    [Fact]
    public async Task AdminUpload_WithDepartments_ShouldRequireBusinessOrg_AndPersistSelectedOrg()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedDepartmentsAsync(factory);
        using var client = factory.CreateClient();

        using var missingOrgContent = await CreateUploadContentAsync("admin-missing-org.docx");
        using var missingOrgResponse = await client.PostAsync("/api/documents/upload", missingOrgContent);
        missingOrgResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var rootOrgContent = await CreateUploadContentAsync("admin-root-org.docx");
        rootOrgContent.Add(
            new StringContent(fixture.RootOrgUnitId.ToString()),
            "businessOrgUnitId");
        using var rootOrgResponse = await client.PostAsync("/api/documents/upload", rootOrgContent);
        rootOrgResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var selectedOrgContent = await CreateUploadContentAsync("admin-department-a.docx");
        selectedOrgContent.Add(
            new StringContent(fixture.DepartmentAId.ToString()),
            "businessOrgUnitId");
        using var selectedOrgResponse = await client.PostAsync("/api/documents/upload", selectedOrgContent);
        selectedOrgResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await selectedOrgResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Data.GetProperty("ownerOrgUnitId").GetInt32().Should().Be(fixture.DepartmentAId);
        body.Data.GetProperty("ownerOrgUnitName").GetString().Should().Be("A部门");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fileId = body.Data.GetProperty("fileId").GetInt32();
        var ownerOrgUnitId = await db.WordFiles
            .Where(file => file.Id == fileId)
            .Select(file => file.OwnerOrgUnitId)
            .SingleAsync();
        ownerOrgUnitId.Should().Be(fixture.DepartmentAId);
    }

    [Fact]
    public async Task CommonUpload_ShouldUseOwnOrg_AndRejectOtherDepartment()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedDepartmentsAsync(factory);
        using var client = factory.CreateClient();

        using var ownOrgRequest = CreateCommonUploadRequest(
            fixture.CommonUserId,
            await CreateUploadContentAsync("common-own-org.docx"));
        using var ownOrgResponse = await client.SendAsync(ownOrgRequest);
        ownOrgResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownOrgBody = await ownOrgResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        ownOrgBody.Data.GetProperty("ownerOrgUnitId").GetInt32().Should().Be(fixture.DepartmentAId);

        var otherOrgContent = await CreateUploadContentAsync("common-other-org.docx");
        otherOrgContent.Add(
            new StringContent(fixture.DepartmentBId.ToString()),
            "businessOrgUnitId");
        using var otherOrgRequest = CreateCommonUploadRequest(fixture.CommonUserId, otherOrgContent);
        using var otherOrgResponse = await client.SendAsync(otherOrgRequest);
        otherOrgResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BusinessContext_ShouldReturnAdminChoices_AndCommonLockedOrg()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedDepartmentsAsync(factory);
        using var client = factory.CreateClient();

        using var adminResponse = await client.GetAsync("/api/org-units/business-context");
        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminBody = await adminResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        adminBody.Data.GetProperty("requiresSelection").GetBoolean().Should().BeTrue();
        adminBody.Data.GetProperty("options").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32())
            .Should().BeEquivalentTo([fixture.DepartmentAId, fixture.DepartmentBId]);

        using var commonRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/org-units/business-context");
        commonRequest.Headers.Add("X-Test-Role", "common");
        commonRequest.Headers.Add("X-Test-User-Id", fixture.CommonUserId.ToString());
        commonRequest.Headers.Add("X-Test-Permissions", "*:*:*");
        using var commonResponse = await client.SendAsync(commonRequest);
        commonResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var commonBody = await commonResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        commonBody.Data.GetProperty("requiresSelection").GetBoolean().Should().BeFalse();
        commonBody.Data.GetProperty("currentOrgUnitId").GetInt32().Should().Be(fixture.DepartmentAId);
        commonBody.Data.GetProperty("currentOrgUnitName").GetString().Should().Be("A部门");
    }

    [Fact]
    public async Task AdminBackfill_ShouldUseSourceFileOrg_AndRejectCrossDepartmentSpec()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedDepartmentsAsync(factory);
        using var client = factory.CreateClient();

        using var uploadContent = await CreateUploadContentAsync("admin-backfill-a.docx");
        uploadContent.Add(
            new StringContent(fixture.DepartmentAId.ToString()),
            "businessOrgUnitId");
        using var uploadResponse = await client.PostAsync("/api/documents/upload", uploadContent);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadBody = await uploadResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var sourceFileId = uploadBody.Data.GetProperty("fileId").GetInt32();

        int customerId;
        int departmentBSpecId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var adminId = await db.SystemUsers
                .Where(user => user.Username == "admin")
                .Select(user => user.Id)
                .SingleAsync();
            var customer = new Customer
            {
                Name = $"隔离客户-{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow
            };
            var departmentBFile = new WordFile
            {
                FileName = "department-b.docx",
                FilePath = "department-b.docx",
                FileHash = Guid.NewGuid().ToString("N"),
                FileType = UploadedFileType.WordDocx,
                FileContent = [1],
                CreatedByUserId = adminId,
                CompanyId = 1,
                OwnerOrgUnitId = fixture.DepartmentBId,
                UploadedAt = DateTime.UtcNow
            };
            db.AddRange(customer, departmentBFile);
            await db.SaveChangesAsync();
            var departmentBSpec = new AcceptanceSpec
            {
                CustomerId = customer.Id,
                Project = "B部门项目",
                Specification = "B部门规格",
                Acceptance = "B部门验收",
                WordFileId = departmentBFile.Id,
                CreatedByUserId = adminId,
                OwnerOrgUnitId = fixture.DepartmentBId,
                ImportedAt = DateTime.UtcNow
            };
            db.AcceptanceSpecs.Add(departmentBSpec);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            departmentBSpecId = departmentBSpec.Id;
        }

        using var crossDepartmentResponse = await client.PostAsync(
            "/api/matching/spec-backfill",
            ApiClientJson.ToJsonContent(new
            {
                fileId = sourceFileId,
                customerId,
                items = new[]
                {
                    new
                    {
                        specId = (int?)departmentBSpecId,
                        overrideAcceptance = "不应修改"
                    }
                }
            }));
        crossDepartmentResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var project = $"A部门新增-{Guid.NewGuid():N}";
        using var createResponse = await client.PostAsync(
            "/api/matching/spec-backfill",
            ApiClientJson.ToJsonContent(new
            {
                fileId = sourceFileId,
                customerId,
                items = new[]
                {
                    new
                    {
                        specId = (int?)null,
                        sourceProject = project,
                        sourceSpecification = "A部门规格",
                        overrideAcceptance = "A部门验收"
                    }
                }
            }));
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var createdOwner = await verifyDb.AcceptanceSpecs
            .Where(spec => spec.Project == project)
            .Select(spec => spec.OwnerOrgUnitId)
            .SingleAsync();
        createdOwner.Should().Be(fixture.DepartmentAId);

        var adminIdForScope = await verifyDb.SystemUsers
            .Where(user => user.Username == "admin")
            .Select(user => user.Id)
            .SingleAsync();
        var sourceFile = await verifyDb.WordFiles.SingleAsync(file => file.Id == sourceFileId);
        var authScopeService = verifyScope.ServiceProvider
            .GetRequiredService<IAuthDataScopeService>();
        var businessScopeService = verifyScope.ServiceProvider
            .GetRequiredService<IBusinessOrgScopeService>();
        var candidateProvider = verifyScope.ServiceProvider
            .GetRequiredService<MatchingCandidateProvider>();
        var adminScope = await authScopeService.GetScopeAsync(
            adminIdForScope,
            1,
            "spec");
        adminScope.Should().NotBeNull();
        var businessScope = await businessScopeService.ResolveFileScopeAsync(
            adminScope!,
            sourceFile);
        var candidates = await candidateProvider.GetCandidatesAsync(
            customerId,
            null,
            null,
            businessScope,
            null,
            hydrateEmbeddings: false);
        candidates.Select(candidate => candidate.Project)
            .Should().Contain(project)
            .And.NotContain("B部门项目");
    }

    [Fact]
    public async Task AdminImport_ShouldPersistSpecsToSelectedBusinessOrg()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedDepartmentsAsync(factory);
        using var client = factory.CreateClient();
        var project = $"导入A项目-{Guid.NewGuid():N}";
        var customerId = 0;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer
            {
                Name = $"导入客户-{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow
            };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }

        using var uploadContent = new MultipartFormDataContent();
        var excelContent = new ByteArrayContent(CreateExcelBytes(project));
        excelContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        uploadContent.Add(excelContent, "file", "department-a-import.xlsx");
        uploadContent.Add(
            new StringContent(fixture.DepartmentAId.ToString()),
            "businessOrgUnitId");
        using var uploadResponse = await client.PostAsync("/api/documents/upload", uploadContent);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadBody = await uploadResponse.ReadAsAsync<ApiResponse<JsonElement>>();

        using var importResponse = await client.PostAsync(
            "/api/documents/excel/import",
            ApiClientJson.ToJsonContent(new
            {
                fileId = uploadBody.Data.GetProperty("fileId").GetInt32(),
                sheetIndex = 0,
                customerId,
                headerRowStart = 1,
                headerRowCount = 1,
                dataStartRow = 2,
                dataEndRow = 2,
                projectColumn = 1,
                specificationColumn = 2,
                acceptanceColumn = 3,
                remarkColumn = 4
            }));
        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownerOrgUnitId = await verifyDb.AcceptanceSpecs
            .Where(spec => spec.Project == project)
            .Select(spec => spec.OwnerOrgUnitId)
            .SingleAsync();
        ownerOrgUnitId.Should().Be(fixture.DepartmentAId);
    }

    private static HttpRequestMessage CreateCommonUploadRequest(
        int userId,
        MultipartFormDataContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/documents/upload")
        {
            Content = content
        };
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        request.Headers.Add("X-Test-Permissions", "*:*:*");
        return request;
    }

    private static async Task<MultipartFormDataContent> CreateUploadContentAsync(string fileName)
    {
        var docPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "example.docx"));
        var bytes = await File.ReadAllBytesAsync(docPath);
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        content.Add(fileContent, "file", fileName);
        return content;
    }

    private static async Task<DepartmentFixture> SeedDepartmentsAsync(
        ApiWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var root = await db.OrgUnits.SingleAsync(org => org.ParentId == null);
        var commonUser = await db.SystemUsers.SingleAsync(user => user.Username == "common");

        var departmentA = CreateDepartment(root, "A部门", now);
        var departmentB = CreateDepartment(root, "B部门", now);
        db.OrgUnits.AddRange(departmentA, departmentB);
        await db.SaveChangesAsync();
        departmentA.Path = $"{root.Path}{departmentA.Id}/";
        departmentB.Path = $"{root.Path}{departmentB.Id}/";

        db.AuthUserOrgUnits.RemoveRange(
            db.AuthUserOrgUnits.Where(link => link.UserId == commonUser.Id));
        db.AuthUserOrgUnits.Add(new AuthUserOrgUnit
        {
            UserId = commonUser.Id,
            OrgUnitId = departmentA.Id,
            IsPrimary = true,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        return new DepartmentFixture(root.Id, commonUser.Id, departmentA.Id, departmentB.Id);
    }

    private static OrgUnit CreateDepartment(OrgUnit root, string name, DateTime now) => new()
    {
        CompanyId = root.CompanyId,
        ParentId = root.Id,
        UnitType = OrgUnitType.Department,
        Code = $"{name}-{Guid.NewGuid():N}"[..28],
        Name = name,
        Path = "/",
        Depth = root.Depth + 1,
        IsActive = true,
        CreatedAt = now
    };

    private static byte[] CreateExcelBytes(string project)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Sheet1");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "验收";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = project;
        worksheet.Cell(2, 2).Value = "A部门规格";
        worksheet.Cell(2, 3).Value = "A部门验收";
        worksheet.Cell(2, 4).Value = "A部门备注";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private sealed record DepartmentFixture(
        int RootOrgUnitId,
        int CommonUserId,
        int DepartmentAId,
        int DepartmentBId);
}
