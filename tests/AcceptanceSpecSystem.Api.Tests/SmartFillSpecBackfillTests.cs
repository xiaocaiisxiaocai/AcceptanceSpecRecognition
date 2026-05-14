using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class SmartFillSpecBackfillTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SmartFillSpecBackfillTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SpecBackfill_ShouldUpdateMatchedSpec_AndCreateManualSpec()
    {
        var setup = await SeedBackfillScopeAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matching/spec-backfill");
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-Permissions", "*:*:*");
        request.Content = ApiClientJson.ToJsonContent(new
        {
            customerId = setup.CustomerId,
            processId = setup.ProcessId,
            machineModelId = setup.MachineModelId,
            items = new BackfillRequestItem[]
            {
                new(
                    SpecId: setup.SpecId,
                    SourceProject: "不应覆盖项目",
                    SourceSpecification: "不应覆盖规格",
                    OverrideAcceptance: "更新后的验收",
                    OverrideRemark: "更新后的备注"),
                new(
                    SpecId: null,
                    SourceProject: "新增项目",
                    SourceSpecification: "新增规格",
                    OverrideAcceptance: "新增验收",
                    OverrideRemark: "新增备注")
            }
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        json.Data.GetProperty("updatedCount").GetInt32().Should().Be(1);
        json.Data.GetProperty("createdCount").GetInt32().Should().Be(1);
        json.Data.GetProperty("totalCount").GetInt32().Should().Be(2);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await db.AcceptanceSpecs.FindAsync(setup.SpecId);
        updated.Should().NotBeNull();
        updated!.Project.Should().Be("原项目");
        updated.Specification.Should().Be("原规格");
        updated.Acceptance.Should().Be("更新后的验收");
        updated.Remark.Should().Be("更新后的备注");

        var cacheExists = await db.EmbeddingCaches.AnyAsync(cache => cache.SpecId == setup.SpecId);
        cacheExists.Should().BeFalse();

        var created = await db.AcceptanceSpecs.SingleAsync(spec =>
            spec.Project == "新增项目" && spec.Specification == "新增规格");
        created.CustomerId.Should().Be(setup.CustomerId);
        created.ProcessId.Should().Be(setup.ProcessId);
        created.MachineModelId.Should().Be(setup.MachineModelId);
        created.Acceptance.Should().Be("新增验收");
        created.Remark.Should().Be("新增备注");
    }

    [Fact]
    public async Task SpecBackfill_WithOutOfScopeSpec_ShouldRejectWithoutPartialWrites()
    {
        var setup = await SeedBackfillScopeAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matching/spec-backfill");
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-Permissions", "*:*:*");
        request.Content = ApiClientJson.ToJsonContent(new
        {
            customerId = setup.CustomerId,
            processId = setup.ProcessId,
            machineModelId = setup.MachineModelId,
            items = new BackfillRequestItem[]
            {
                new(
                    SpecId: setup.OutOfScopeSpecId,
                    SourceProject: null,
                    SourceSpecification: null,
                    OverrideAcceptance: "不应写入",
                    OverrideRemark: "不应写入"),
                new(
                    SpecId: null,
                    SourceProject: "不应新增项目",
                    SourceSpecification: "不应新增规格",
                    OverrideAcceptance: "不应新增验收",
                    OverrideRemark: null)
            }
        });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(403);
        json.Message.Should().Contain("无权");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outOfScope = await db.AcceptanceSpecs.FindAsync(setup.OutOfScopeSpecId);
        outOfScope.Should().NotBeNull();
        outOfScope!.Acceptance.Should().Be("范围外验收");
        var created = await db.AcceptanceSpecs.AnyAsync(spec => spec.Project == "不应新增项目");
        created.Should().BeFalse();
    }

    private async Task<BackfillSetup> SeedBackfillScopeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var customer = new Customer { Name = $"回填客户-{suffix}", CreatedAt = DateTime.UtcNow };
        var process = new Process { Name = $"回填制程-{suffix}", CreatedAt = DateTime.UtcNow };
        var machineModel = new MachineModel { Name = $"回填机型-{suffix}", CreatedAt = DateTime.UtcNow };
        var wordFile = new WordFile
        {
            FileName = $"backfill-{suffix}.docx",
            FileContent = [],
            FileHash = Guid.NewGuid().ToString("N"),
            UploadedAt = DateTime.UtcNow
        };
        var rootOrgUnitId = await db.OrgUnits
            .Where(org => org.ParentId == null)
            .Select(org => org.Id)
            .FirstAsync();
        var outOfScopeOrg = new OrgUnit
        {
            CompanyId = 1,
            ParentId = rootOrgUnitId,
            UnitType = OrgUnitType.Division,
            Code = $"BACKFILL-OUT-{suffix}",
            Name = $"回填范围外组织-{suffix}",
            Path = $"/{rootOrgUnitId}/",
            Depth = 1,
            Sort = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Customers.Add(customer);
        db.Processes.Add(process);
        db.MachineModels.Add(machineModel);
        db.WordFiles.Add(wordFile);
        db.OrgUnits.Add(outOfScopeOrg);
        await db.SaveChangesAsync();
        outOfScopeOrg.Path = $"/{rootOrgUnitId}/{outOfScopeOrg.Id}/";
        await ConfigureCommonSpecScopeAsync(db, rootOrgUnitId);

        var spec = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            MachineModelId = machineModel.Id,
            Project = "原项目",
            Specification = "原规格",
            Acceptance = "原验收",
            Remark = "原备注",
            WordFileId = wordFile.Id,
            OwnerOrgUnitId = rootOrgUnitId,
            CreatedByUserId = 1,
            ImportedAt = DateTime.UtcNow
        };
        var outOfScopeSpec = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            MachineModelId = machineModel.Id,
            Project = "范围外项目",
            Specification = "范围外规格",
            Acceptance = "范围外验收",
            Remark = "范围外备注",
            WordFileId = wordFile.Id,
            OwnerOrgUnitId = outOfScopeOrg.Id,
            CreatedByUserId = 1,
            ImportedAt = DateTime.UtcNow
        };

        db.AcceptanceSpecs.AddRange(spec, outOfScopeSpec);
        await db.SaveChangesAsync();
        db.EmbeddingCaches.Add(new EmbeddingCache
        {
            SpecId = spec.Id,
            ModelName = "test-embedding",
            Vector = [1, 2, 3],
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return new BackfillSetup(customer.Id, process.Id, machineModel.Id, spec.Id, outOfScopeSpec.Id);
    }

    private static async Task ConfigureCommonSpecScopeAsync(AppDbContext db, int orgUnitId)
    {
        var commonRoleId = await db.AuthRoles
            .Where(role => role.Code == "common")
            .Select(role => role.Id)
            .FirstAsync();
        var roleScopes = await db.AuthRoleDataScopes
            .Include(scope => scope.Nodes)
            .Where(scope => scope.RoleId == commonRoleId && scope.Resource == "spec")
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
            db.AuthRoleDataScopes.Add(roleScope);
        }
        else
        {
            roleScope.ScopeType = DataScopeType.CustomNodes;
            db.AuthRoleDataScopeNodes.RemoveRange(roleScope.Nodes);
            roleScope.Nodes.Clear();

            if (roleScopes.Count > 1)
            {
                db.AuthRoleDataScopes.RemoveRange(roleScopes.Skip(1));
            }
        }

        roleScope.Nodes.Add(new AuthRoleDataScopeNode { OrgUnitId = orgUnitId });
        await db.SaveChangesAsync();
    }

    private sealed record BackfillSetup(
        int CustomerId,
        int ProcessId,
        int MachineModelId,
        int SpecId,
        int OutOfScopeSpecId);

    private sealed record BackfillRequestItem(
        int? SpecId,
        string? SourceProject,
        string? SourceSpecification,
        string? OverrideAcceptance,
        string? OverrideRemark);
}
