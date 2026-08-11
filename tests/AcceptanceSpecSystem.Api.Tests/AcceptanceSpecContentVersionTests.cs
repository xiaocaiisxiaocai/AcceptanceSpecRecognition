using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class AcceptanceSpecContentVersionTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AcceptanceSpecContentVersionTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateUpdateDiffAndRestore_ShouldPreserveImmutableContentHistory()
    {
        var specId = await CreateSpecAsync("原项目", "原规格", "原验收", "原备注");

        var initialHistory = await GetHistoryAsync(specId, "oldest");
        initialHistory.GetProperty("currentVersion").GetInt64().Should().Be(1);
        initialHistory.GetProperty("total").GetInt32().Should().Be(1);
        initialHistory.GetProperty("items")[0].GetProperty("changeSource").GetString()
            .Should().Be("create");

        var identical = await UpdateSpecAsync(specId, 1, "原项目", "原规格", "原验收", "原备注");
        identical.StatusCode.Should().Be(HttpStatusCode.OK);
        var identicalBody = await identical.ReadAsAsync<ApiResponse<JsonElement>>();
        identicalBody.Data.GetProperty("referenceVersion").GetInt64().Should().Be(1);

        var changed = await UpdateSpecAsync(specId, 1, "新项目", "新规格", "新验收", "新备注", "修正内容");
        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        var changedBody = await changed.ReadAsAsync<ApiResponse<JsonElement>>();
        changedBody.Data.GetProperty("referenceVersion").GetInt64().Should().Be(2);

        var stale = await UpdateSpecAsync(specId, 1, "过期项目", "新规格", "新验收", "新备注");
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var history = await GetHistoryAsync(specId, "oldest");
        history.GetProperty("total").GetInt32().Should().Be(2);
        history.GetProperty("items")[1].GetProperty("changeReason").GetString()
            .Should().Be("修正内容");

        var detail = await _client.GetAsync($"/api/specs/{specId}/content-versions/1");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailBody = await detail.ReadAsAsync<ApiResponse<JsonElement>>();
        detailBody.Data.GetProperty("project").GetString().Should().Be("原项目");
        detailBody.Data.GetProperty("specification").GetString().Should().Be("原规格");

        var diff = await _client.GetAsync(
            $"/api/specs/{specId}/content-version-diff?fromVersion=1&toVersion=2");
        diff.StatusCode.Should().Be(HttpStatusCode.OK);
        var diffBody = await diff.ReadAsAsync<ApiResponse<JsonElement>>();
        diffBody.Data.GetProperty("fields").GetProperty("project")
            .GetProperty("changed").GetBoolean().Should().BeTrue();
        diffBody.Data.GetProperty("fields").GetProperty("project")
            .GetProperty("before").GetString().Should().Be("原项目");
        diffBody.Data.GetProperty("fields").GetProperty("project")
            .GetProperty("after").GetString().Should().Be("新项目");

        var restore = await _client.PostAsync(
            $"/api/specs/{specId}/content-versions/1/restore",
            ApiClientJson.ToJsonContent(new
            {
                expectedCurrentVersion = 2,
                reason = "恢复误改前内容"
            }));
        restore.StatusCode.Should().Be(HttpStatusCode.OK);
        var restoreBody = await restore.ReadAsAsync<ApiResponse<JsonElement>>();
        restoreBody.Data.GetProperty("referenceVersion").GetInt64().Should().Be(3);
        restoreBody.Data.GetProperty("project").GetString().Should().Be("原项目");

        var restoredHistory = await GetHistoryAsync(specId, "oldest");
        restoredHistory.GetProperty("total").GetInt32().Should().Be(3);
        restoredHistory.GetProperty("items")[2].GetProperty("restoredFromVersion")
            .GetInt64().Should().Be(1);
        restoredHistory.GetProperty("items")[2].GetProperty("changeReason")
            .GetString().Should().Be("恢复误改前内容");
    }

    [Fact]
    public async Task ContentHistory_ShouldValidateSortPageAndMissingVersion()
    {
        var specId = await CreateSpecAsync("校验项目", "校验规格", null, null);

        (await _client.GetAsync($"/api/specs/{specId}/content-versions?sort=sideways"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.GetAsync($"/api/specs/{specId}/content-versions?pageSize=101"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.GetAsync($"/api/specs/{specId}/content-versions/99"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ContentHistory_ShouldPreserveChangedFieldsAcrossPageBoundaries()
    {
        var specId = await CreateSpecAsync("分页项目-1", "分页规格", null, null);
        for (var version = 1; version < 5; version++)
        {
            var response = await UpdateSpecAsync(
                specId,
                version,
                $"分页项目-{version + 1}",
                "分页规格",
                null,
                null);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var responsePage = await _client.GetAsync(
            $"/api/specs/{specId}/content-versions?page=2&pageSize=2&sort=newest");
        responsePage.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await responsePage.ReadAsAsync<ApiResponse<JsonElement>>()).Data;
        page.GetProperty("total").GetInt32().Should().Be(5);
        page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("version").GetInt64())
            .Should().Equal(3, 2);
        page.GetProperty("items")[0].GetProperty("changedFields").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Equal("project");
        page.GetProperty("items")[1].GetProperty("changedFields").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Equal("project");
    }

    [Fact]
    public async Task ReadPermission_ShouldAllowHistoryButNotRestore()
    {
        var specId = await CreateSpecAsync("权限项目", "权限规格", null, null);
        var changed = await UpdateSpecAsync(specId, 1, "权限项目-更新", "权限规格", null, null);
        changed.StatusCode.Should().Be(HttpStatusCode.OK);

        using var readRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/specs/{specId}/content-versions");
        readRequest.Headers.Add("X-Test-Permissions", "api:spec:read");
        using var readResponse = await _client.SendAsync(readRequest);
        readResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var restoreRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/specs/{specId}/content-versions/1/restore")
        {
            Content = ApiClientJson.ToJsonContent(new { expectedCurrentVersion = 2 })
        };
        restoreRequest.Headers.Add("X-Test-Permissions", "api:spec:read");
        using var restoreResponse = await _client.SendAsync(restoreRequest);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ApiBatchImport_ShouldCreateInitialContentSnapshot()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var customerResponse = await _client.PostAsync(
            "/api/customers",
            ApiClientJson.ToJsonContent(new { name = $"BatchVersion-C-{suffix}" }));
        var customer = await customerResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var customerId = customer.Data.GetProperty("id").GetInt32();

        int wordFileId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var root = await db.OrgUnits.SingleAsync(orgUnit => orgUnit.ParentId == null);
            var department = new OrgUnit
            {
                CompanyId = root.CompanyId,
                ParentId = root.Id,
                Code = $"batch-version-{suffix}",
                Name = "批量版本测试部门",
                Path = $"{root.Path}pending/",
                Depth = root.Depth + 1,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.OrgUnits.Add(department);
            await db.SaveChangesAsync();
            department.Path = $"{root.Path}{department.Id}/";
            var file = new WordFile
            {
                CompanyId = root.CompanyId,
                CreatedByUserId = 1,
                OwnerOrgUnitId = department.Id,
                FileName = $"batch-version-{suffix}.docx",
                FileContent = [],
                FileHash = suffix,
                UploadedAt = DateTime.UtcNow
            };
            db.WordFiles.Add(file);
            await db.SaveChangesAsync();
            wordFileId = file.Id;
        }

        var response = await _client.PostAsync(
            "/api/specs/batch-import",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                wordFileId,
                items = new[]
                {
                    new
                    {
                        project = "批量导入项目",
                        specification = "批量导入规格",
                        acceptance = "批量导入验收",
                        remark = "批量导入备注"
                    }
                }
            }));
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var spec = await verifyDb.AcceptanceSpecs.SingleAsync(item =>
            item.WordFileId == wordFileId && item.Project == "批量导入项目");
        var snapshot = await verifyDb.AcceptanceSpecContentVersions
            .SingleAsync(version => version.AcceptanceSpecId == spec.Id);
        snapshot.Version.Should().Be(1);
        snapshot.ChangeSource.Should().Be("create");
        snapshot.Specification.Should().Be("批量导入规格");
    }

    private async Task<JsonElement> GetHistoryAsync(int specId, string sort)
    {
        var response = await _client.GetAsync(
            $"/api/specs/{specId}/content-versions?page=1&pageSize=20&sort={sort}");
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        return (await response.ReadAsAsync<ApiResponse<JsonElement>>()).Data;
    }

    private async Task<int> CreateSpecAsync(
        string project,
        string specification,
        string? acceptance,
        string? remark)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var customerResponse = await _client.PostAsync(
            "/api/customers",
            ApiClientJson.ToJsonContent(new { name = $"ContentVersion-C-{suffix}" }));
        var customer = await customerResponse.ReadAsAsync<ApiResponse<JsonElement>>();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var businessOrgUnitId = await db.OrgUnits
            .Where(orgUnit => orgUnit.ParentId != null)
            .Select(orgUnit => orgUnit.Id)
            .FirstAsync();

        var response = await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                businessOrgUnitId,
                customerId = customer.Data.GetProperty("id").GetInt32(),
                project,
                specification,
                acceptance,
                remark
            }));
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return body.Data.GetProperty("id").GetInt32();
    }

    private Task<HttpResponseMessage> UpdateSpecAsync(
        int specId,
        long expectedReferenceVersion,
        string project,
        string specification,
        string? acceptance,
        string? remark,
        string? changeReason = null)
    {
        return _client.PutAsync(
            $"/api/specs/{specId}",
            ApiClientJson.ToJsonContent(new
            {
                expectedReferenceVersion,
                project,
                specification,
                acceptance,
                remark,
                changeReason
            }));
    }
}
