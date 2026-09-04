using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class SmartFillArchiveAuthorizationTests
{
    [Fact]
    public async Task CommonUser_ShouldOnlyQueryItsDepartmentArchives()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedTwoDepartmentsAsync(factory);
        using var client = factory.CreateClient();
        using var request = CreateCommonRequest(
            $"/api/execution-history/smart-fill-archives?page=1&pageSize=20",
            fixture.CommonUserId);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var items = body.Data.GetProperty("items").EnumerateArray().ToList();
        items.Should().ContainSingle();
        items[0].GetProperty("sourceFileName").GetString().Should().Be("department-a.xlsx");
        items[0].GetProperty("ownerOrgUnitName").GetString().Should().Be("A部门");
    }

    [Fact]
    public async Task Admin_ShouldQueryCompanyArchives_AndFilterByDepartment()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedTwoDepartmentsAsync(factory);
        using var client = factory.CreateClient();

        using var overall = await client.GetAsync(
            "/api/execution-history/smart-fill-archives?page=1&pageSize=20");
        overall.StatusCode.Should().Be(HttpStatusCode.OK);
        var overallBody = await overall.ReadAsAsync<ApiResponse<JsonElement>>();
        overallBody.Data.GetProperty("total").GetInt32().Should().Be(2);

        using var filtered = await client.GetAsync(
            $"/api/execution-history/smart-fill-archives?page=1&pageSize=20&orgUnitId={fixture.DepartmentBId}");
        filtered.StatusCode.Should().Be(HttpStatusCode.OK);
        var filteredBody = await filtered.ReadAsAsync<ApiResponse<JsonElement>>();
        var items = filteredBody.Data.GetProperty("items").EnumerateArray().ToList();
        items.Should().ContainSingle();
        items[0].GetProperty("sourceFileName").GetString().Should().Be("department-b.xlsx");
    }

    [Fact]
    public async Task CommonUser_ShouldNotDownloadOtherDepartmentArchive()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedTwoDepartmentsAsync(factory);
        using var client = factory.CreateClient();
        using var request = CreateCommonRequest(
            $"/api/execution-history/smart-fill-archives/{fixture.DepartmentBHistoryId}/download",
            fixture.CommonUserId);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CommonUser_ShouldOnlyQueryOwnLegacyArchiveWithoutDepartment()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedTwoDepartmentsAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var adminUserId = await db.SystemUsers
                .Where(user => user.Username == "admin")
                .Select(user => user.Id)
                .SingleAsync();
            db.ExecutionHistoryRecords.AddRange(
                CreateHistory("legacy-own", "legacy-own.xlsx", fixture.CommonUserId, null, DateTime.UtcNow),
                CreateHistory("legacy-other", "legacy-other.xlsx", adminUserId, null, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        using var request = CreateCommonRequest(
            "/api/execution-history/smart-fill-archives?page=1&pageSize=20&keyword=legacy-",
            fixture.CommonUserId);
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var items = body.Data.GetProperty("items").EnumerateArray().ToList();
        items.Should().ContainSingle();
        items[0].GetProperty("sourceFileName").GetString().Should().Be("legacy-own.xlsx");
    }

    [Fact]
    public async Task Download_ShouldRejectLegacyRecordAndTamperedArchive()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedTwoDepartmentsAsync(factory);
        using var client = factory.CreateClient();

        using (var legacyRequest = CreateCommonRequest(
                   $"/api/execution-history/smart-fill-archives/{fixture.DepartmentAHistoryId}/download",
                   fixture.CommonUserId))
        using (var legacyResponse = await client.SendAsync(legacyRequest))
        {
            legacyResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        const string original = "archive";
        var bytes = System.Text.Encoding.UTF8.GetBytes(original);
        string path;
        using (var scope = factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            path = await storage.SaveSmartFillResultArchiveAsync("department-a.xlsx", bytes);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.ExecutionHistoryRecords.SingleAsync(
                item => item.Id == fixture.DepartmentAHistoryId);
            record.ResultArchiveRelativePath = path;
            record.ResultArchiveFileName = "department-a.xlsx";
            record.ResultArchiveContentType =
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            record.ResultArchiveSizeBytes = bytes.LongLength;
            record.ResultArchiveSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            await db.SaveChangesAsync();
            await File.WriteAllTextAsync(storage.GetAbsolutePath(path), "tamper!");
        }

        using var request = CreateCommonRequest(
            $"/api/execution-history/smart-fill-archives/{fixture.DepartmentAHistoryId}/download",
            fixture.CommonUserId);
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("完整性");
    }

    [Fact]
    public async Task Download_ShouldRejectInvalidNamespaceAndMissingArchiveFile()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedTwoDepartmentsAsync(factory);
        using var client = factory.CreateClient();
        var bytes = System.Text.Encoding.UTF8.GetBytes("archive");
        string allowedPath;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.ExecutionHistoryRecords.SingleAsync(
                item => item.Id == fixture.DepartmentAHistoryId);
            SetArchiveMetadata(record, "uploads/word-files/2026-01-01/invalid.xlsx", bytes);
            await db.SaveChangesAsync();
        }

        using (var invalidRequest = CreateCommonRequest(
                   $"/api/execution-history/smart-fill-archives/{fixture.DepartmentAHistoryId}/download",
                   fixture.CommonUserId))
        using (var invalidResponse = await client.SendAsync(invalidRequest))
        {
            invalidResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            allowedPath = await storage.SaveSmartFillResultArchiveAsync("missing.xlsx", bytes);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.ExecutionHistoryRecords.SingleAsync(
                item => item.Id == fixture.DepartmentAHistoryId);
            SetArchiveMetadata(record, allowedPath, bytes);
            await db.SaveChangesAsync();
            await storage.DeleteIfExistsAsync(allowedPath);
        }

        using var missingRequest = CreateCommonRequest(
            $"/api/execution-history/smart-fill-archives/{fixture.DepartmentAHistoryId}/download",
            fixture.CommonUserId);
        using var missingResponse = await client.SendAsync(missingRequest);
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_ShouldNotQueryOtherCompanyArchives()
    {
        await using var factory = new ApiWebApplicationFactory();
        await SeedTwoDepartmentsAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var company = new OrgCompany
            {
                Code = $"archive-other-{Guid.NewGuid():N}"[..28],
                Name = "其他公司",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.OrgCompanies.Add(company);
            await db.SaveChangesAsync();
            var admin = await db.SystemUsers.SingleAsync(user => user.Username == "admin");
            var otherCompanyRecord = CreateHistory(
                "archive-other-company",
                "other-company.xlsx",
                admin.Id,
                orgUnitId: null,
                DateTime.UtcNow);
            otherCompanyRecord.CompanyId = company.Id;
            db.ExecutionHistoryRecords.Add(otherCompanyRecord);
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            "/api/execution-history/smart-fill-archives?page=1&pageSize=20&keyword=other-company.xlsx");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Data.GetProperty("total").GetInt32().Should().Be(0);
    }

    private static HttpRequestMessage CreateCommonRequest(string uri, int userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        request.Headers.Add(
            "X-Test-Permissions",
            "api:execution-history:read,api:execution-history:download");
        return request;
    }

    private static async Task<ArchiveDepartmentFixture> SeedTwoDepartmentsAsync(
        ApiWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var root = await db.OrgUnits.SingleAsync(org => org.ParentId == null);
        var commonRole = await db.AuthRoles.SingleAsync(role => role.Code == "common");
        var commonUser = await db.SystemUsers.SingleAsync(user => user.Username == "common");
        var adminUser = await db.SystemUsers.SingleAsync(user => user.Username == "admin");

        var departmentA = CreateDepartment(root, "A部门");
        var departmentB = CreateDepartment(root, "B部门");
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
        db.AuthRoleDataScopes.RemoveRange(
            db.AuthRoleDataScopes.Where(item => item.RoleId == commonRole.Id));
        db.AuthRoleDataScopes.Add(new AuthRoleDataScope
        {
            RoleId = commonRole.Id,
            Resource = "spec",
            ScopeType = DataScopeType.OrgSubtree,
            CreatedAt = now
        });

        var departmentAHistory = CreateHistory(
            "archive-a", "department-a.xlsx", commonUser.Id, departmentA.Id, now);
        var departmentBHistory = CreateHistory(
            "archive-b", "department-b.xlsx", adminUser.Id, departmentB.Id, now.AddMinutes(-1));
        db.ExecutionHistoryRecords.AddRange(departmentAHistory, departmentBHistory);
        await db.SaveChangesAsync();

        return new ArchiveDepartmentFixture(
            commonUser.Id,
            departmentB.Id,
            departmentAHistory.Id,
            departmentBHistory.Id);
    }

    private static OrgUnit CreateDepartment(OrgUnit root, string name) => new()
    {
        CompanyId = root.CompanyId,
        ParentId = root.Id,
        UnitType = OrgUnitType.Department,
        Code = $"archive-{Guid.NewGuid():N}"[..28],
        Name = name,
        Path = "/",
        Depth = root.Depth + 1,
        IsActive = true,
        CreatedAt = DateTime.UtcNow
    };

    private static ExecutionHistoryRecord CreateHistory(
        string taskId,
        string fileName,
        int userId,
        int? orgUnitId,
        DateTime createdAt) => new()
        {
            TaskId = taskId,
            TaskType = "smart-fill",
            SourceFileName = fileName,
            SourceFileType = UploadedFileType.ExcelXlsx,
            FileCount = 1,
            TotalRowCount = 3,
            MatchedRowCount = 2,
            AdoptedRowCount = 2,
            UnmatchedRowCount = 1,
            CreatedByUserId = userId,
            CompanyId = 1,
            OwnerOrgUnitId = orgUnitId,
            DetailJson = "{}",
            CreatedAt = createdAt
        };

    private static void SetArchiveMetadata(
        ExecutionHistoryRecord record,
        string relativePath,
        byte[] content)
    {
        record.ResultArchiveRelativePath = relativePath;
        record.ResultArchiveFileName = "archive.xlsx";
        record.ResultArchiveContentType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        record.ResultArchiveSizeBytes = content.LongLength;
        record.ResultArchiveSha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    private sealed record ArchiveDepartmentFixture(
        int CommonUserId,
        int DepartmentBId,
        int DepartmentAHistoryId,
        int DepartmentBHistoryId);
}
