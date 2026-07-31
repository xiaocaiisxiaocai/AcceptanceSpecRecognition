using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class DepartmentDashboardAuthorizationTests
{
    [Fact]
    public async Task CommonDashboard_ShouldAggregateItsDepartment_AndRejectDepartmentOverride()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedTwoDepartmentDashboardAsync(factory);
        using var client = factory.CreateClient();

        using var request = CreateCommonRequest(
            HttpMethod.Get,
            "/api/dashboard/summary",
            fixture.CommonUserId);
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Data.GetProperty("specTotal").GetInt32().Should().Be(1);
        body.Data.GetProperty("smartFillTaskCount").GetInt32().Should().Be(1);
        body.Data.GetProperty("recentExecutions").GetArrayLength().Should().Be(1);
        body.Data.GetProperty("recentExecutions")[0].GetProperty("sourceFileName")
            .GetString().Should().Be("department-a.xlsx");

        using var overrideRequest = CreateCommonRequest(
            HttpMethod.Get,
            $"/api/dashboard/summary?orgUnitId={fixture.DepartmentBId}",
            fixture.CommonUserId);
        using var overrideResponse = await client.SendAsync(overrideRequest);
        overrideResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminDashboard_ShouldSupportCompanyTotalAndSingleDepartmentFilter()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedTwoDepartmentDashboardAsync(factory);
        using var client = factory.CreateClient();

        using var overall = await client.GetAsync("/api/dashboard/summary");
        overall.StatusCode.Should().Be(HttpStatusCode.OK);
        var overallBody = await overall.ReadAsAsync<ApiResponse<JsonElement>>();
        overallBody.Data.GetProperty("specTotal").GetInt32().Should().Be(2);
        overallBody.Data.GetProperty("smartFillTaskCount").GetInt32().Should().Be(2);
        overallBody.Data.GetProperty("recentExecutions").GetArrayLength().Should().Be(2);

        using var department = await client.GetAsync(
            $"/api/dashboard/summary?orgUnitId={fixture.DepartmentBId}");
        department.StatusCode.Should().Be(HttpStatusCode.OK);
        var departmentBody = await department.ReadAsAsync<ApiResponse<JsonElement>>();
        departmentBody.Data.GetProperty("specTotal").GetInt32().Should().Be(1);
        departmentBody.Data.GetProperty("smartFillTaskCount").GetInt32().Should().Be(1);
        departmentBody.Data.GetProperty("recentExecutions")[0].GetProperty("sourceFileName")
            .GetString().Should().Be("department-b.xlsx");
    }

    [Fact]
    public async Task CommonSystemUserManager_ShouldNotSeeOrAssignAdmin()
    {
        await using var factory = new ApiWebApplicationFactory();
        var fixture = await SeedTwoDepartmentDashboardAsync(factory);
        using var client = factory.CreateClient();

        using var listRequest = CreateCommonRequest(
            HttpMethod.Get,
            "/api/system-users?page=1&pageSize=50",
            fixture.CommonUserId,
            "api:system-user:read");
        using var listResponse = await client.SendAsync(listRequest);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResponse.ReadAsAsync<ApiResponse<PagedData<JsonElement>>>();
        list.Data!.Items.Should().OnlyContain(item =>
            item.GetProperty("roleCode").GetString() != "admin" &&
            item.GetProperty("orgUnitId").GetInt32() == fixture.DepartmentAId);

        using var createRequest = CreateCommonRequest(
            HttpMethod.Post,
            "/api/system-users",
            fixture.CommonUserId,
            "api:system-user:create");
        createRequest.Content = ApiClientJson.ToJsonContent(new
        {
            username = $"blocked_admin_{Guid.NewGuid():N}"[..28],
            password = "Admin@1234567",
            nickname = "不应创建",
            roleCode = "admin",
            orgUnitId = fixture.DepartmentAId,
            isActive = true
        });
        using var createResponse = await client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var roleRequest = CreateCommonRequest(
            HttpMethod.Get,
            "/api/auth-roles",
            fixture.CommonUserId,
            "api:auth-role:read");
        using var roleResponse = await client.SendAsync(roleRequest);
        roleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var roles = await roleResponse.ReadAsAsync<ApiResponse<List<JsonElement>>>();
        roles.Data.Should().ContainSingle();
        roles.Data![0].GetProperty("code").GetString().Should().Be("common");

        int adminUserId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            adminUserId = await db.SystemUsers
                .Where(user => user.Username == "admin")
                .Select(user => user.Id)
                .SingleAsync();
        }
        using var statusRequest = CreateCommonRequest(
            HttpMethod.Put,
            $"/api/system-users/{adminUserId}/status",
            fixture.CommonUserId,
            "api:system-user:update-status");
        statusRequest.Content = ApiClientJson.ToJsonContent(new { isActive = false });
        using var statusResponse = await client.SendAsync(statusRequest);
        statusResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static HttpRequestMessage CreateCommonRequest(
        HttpMethod method,
        string uri,
        int userId,
        string permissions = "api:dashboard:read")
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        request.Headers.Add("X-Test-Permissions", permissions);
        return request;
    }

    private static async Task<DepartmentFixture> SeedTwoDepartmentDashboardAsync(
        ApiWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var root = await db.OrgUnits.SingleAsync(org => org.ParentId == null);
        var commonRole = await db.AuthRoles.SingleAsync(role => role.Code == "common");
        var commonUser = await db.SystemUsers.SingleAsync(user => user.Username == "common");

        var departmentA = new OrgUnit
        {
            CompanyId = root.CompanyId,
            ParentId = root.Id,
            UnitType = OrgUnitType.Department,
            Code = $"dept-a-{Guid.NewGuid():N}"[..28],
            Name = "A部门",
            Path = "/",
            Depth = root.Depth + 1,
            IsActive = true,
            CreatedAt = now
        };
        var departmentB = new OrgUnit
        {
            CompanyId = root.CompanyId,
            ParentId = root.Id,
            UnitType = OrgUnitType.Department,
            Code = $"dept-b-{Guid.NewGuid():N}"[..28],
            Name = "B部门",
            Path = "/",
            Depth = root.Depth + 1,
            IsActive = true,
            CreatedAt = now
        };
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

        var departmentBUser = new SystemUser
        {
            CompanyId = root.CompanyId,
            Username = $"dept_b_{Guid.NewGuid():N}"[..28],
            PasswordHash = "unused",
            Nickname = "B部门用户",
            IsActive = true,
            PermissionVersion = 1,
            CreatedAt = now
        };
        db.SystemUsers.Add(departmentBUser);
        await db.SaveChangesAsync();
        db.AuthUserRoles.Add(new AuthUserRole
        {
            UserId = departmentBUser.Id,
            RoleId = commonRole.Id,
            CreatedAt = now
        });
        db.AuthUserOrgUnits.Add(new AuthUserOrgUnit
        {
            UserId = departmentBUser.Id,
            OrgUnitId = departmentB.Id,
            IsPrimary = true,
            CreatedAt = now
        });

        var customerA = new Customer { Name = $"客户A-{Guid.NewGuid():N}", CreatedAt = now };
        var customerB = new Customer { Name = $"客户B-{Guid.NewGuid():N}", CreatedAt = now };
        db.Customers.AddRange(customerA, customerB);
        await db.SaveChangesAsync();

        var fileA = CreateWordFile("department-a.docx", commonUser.Id, departmentA.Id, now);
        var fileB = CreateWordFile("department-b.docx", departmentBUser.Id, departmentB.Id, now);
        db.WordFiles.AddRange(fileA, fileB);
        await db.SaveChangesAsync();
        db.AcceptanceSpecs.AddRange(
            CreateSpec(customerA.Id, fileA.Id, commonUser.Id, departmentA.Id, "A规格", now),
            CreateSpec(customerB.Id, fileB.Id, departmentBUser.Id, departmentB.Id, "B规格", now));
        db.ExecutionHistoryRecords.AddRange(
            CreateHistory("department-a.xlsx", commonUser.Id, now),
            CreateHistory("department-b.xlsx", departmentBUser.Id, now.AddMinutes(-1)));
        await db.SaveChangesAsync();

        return new DepartmentFixture(commonUser.Id, departmentA.Id, departmentB.Id);
    }

    private static WordFile CreateWordFile(
        string fileName,
        int userId,
        int orgUnitId,
        DateTime now) => new()
        {
            FileName = fileName,
            FilePath = fileName,
            FileHash = Guid.NewGuid().ToString("N"),
            FileType = UploadedFileType.WordDocx,
            FileContent = [1],
            CreatedByUserId = userId,
            CompanyId = 1,
            OwnerOrgUnitId = orgUnitId,
            UploadedAt = now
        };

    private static AcceptanceSpec CreateSpec(
        int customerId,
        int wordFileId,
        int userId,
        int orgUnitId,
        string project,
        DateTime now) => new()
        {
            CustomerId = customerId,
            Project = project,
            Specification = project,
            Acceptance = "OK",
            Remark = "-",
            WordFileId = wordFileId,
            CreatedByUserId = userId,
            OwnerOrgUnitId = orgUnitId,
            ImportedAt = now
        };

    private static ExecutionHistoryRecord CreateHistory(
        string fileName,
        int userId,
        DateTime now) => new()
        {
            TaskId = Guid.NewGuid().ToString("N"),
            TaskType = "smart-fill",
            SourceFileName = fileName,
            SourceFileType = UploadedFileType.ExcelXlsx,
            FileCount = 1,
            TotalRowCount = 10,
            MatchedRowCount = 8,
            AdoptedRowCount = 6,
            UnmatchedRowCount = 2,
            NotAdoptedRowCount = 2,
            DetailJson = "{}",
            CreatedByUserId = userId,
            CompanyId = 1,
            CreatedAt = now
        };

    private sealed record DepartmentFixture(
        int CommonUserId,
        int DepartmentAId,
        int DepartmentBId);
}
