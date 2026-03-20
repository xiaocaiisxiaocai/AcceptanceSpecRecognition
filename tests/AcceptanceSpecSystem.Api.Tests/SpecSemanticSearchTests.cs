using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class SpecSemanticSearchTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SpecSemanticSearchTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SemanticSearch_ShouldReturnGroupedTopHits()
    {
        var seeded = await SeedSemanticSearchSpecsAsync();

        using var response = await _client.PostAsync(
            "/api/specs/semantic-search",
            ApiClientJson.ToJsonContent(new
            {
                queries = new[]
                {
                    seeded.Query1,
                    seeded.Query2
                },
                customerId = seeded.CustomerId,
                processId = seeded.ProcessId,
                machineModelIdIsNull = true,
                topK = 2,
                minScore = 0.5
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        json.Data.GetProperty("queryCount").GetInt32().Should().Be(2);
        json.Data.GetProperty("candidateCount").GetInt32().Should().BeGreaterThanOrEqualTo(3);

        var groups = json.Data.GetProperty("groups");
        groups.GetArrayLength().Should().Be(2);
        groups[0].GetProperty("queryText").GetString().Should().Be(seeded.Query1);
        groups[1].GetProperty("queryText").GetString().Should().Be(seeded.Query2);
        groups[0].GetProperty("items")[0].GetProperty("id").GetInt32().Should().Be(seeded.Spec1Id);
        groups[1].GetProperty("items")[0].GetProperty("id").GetInt32().Should().Be(seeded.Spec2Id);
        groups[0].GetProperty("items")[0].GetProperty("score").GetDouble().Should().BeGreaterThan(0.99);
        groups[1].GetProperty("items")[0].GetProperty("score").GetDouble().Should().BeGreaterThan(0.99);
    }

    [Fact]
    public async Task SemanticSearch_ShouldReturnEmptyGroupWhenThresholdEliminatesAllHits()
    {
        var seeded = await SeedSemanticSearchSpecsAsync();

        using var response = await _client.PostAsync(
            "/api/specs/semantic-search",
            ApiClientJson.ToJsonContent(new
            {
                queries = new[] { "完全不相关的英文 query only" },
                customerId = seeded.CustomerId,
                processId = seeded.ProcessId,
                machineModelIdIsNull = true,
                topK = 5,
                minScore = 0.999999
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        var group = json.Data.GetProperty("groups")[0];
        group.GetProperty("totalHits").GetInt32().Should().Be(0);
        group.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task SemanticSearch_ShouldOnlyReturnScopedSpecs()
    {
        var seeded = await SeedScopedSemanticSearchSpecsAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/specs/semantic-search");
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-Permissions", "*:*:*");
        request.Content = ApiClientJson.ToJsonContent(new
        {
            queries = new[] { seeded.Query },
            customerId = seeded.CustomerId,
            processId = seeded.ProcessId,
            machineModelIdIsNull = true,
            topK = 5,
            minScore = 0.5
        });

        using var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        var items = json.Data.GetProperty("groups")[0].GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("id").GetInt32().Should().Be(seeded.InScopeSpecId);
    }

    [Fact]
    public async Task SemanticSearch_ShouldRejectEmptyQueries()
    {
        using var response = await _client.PostAsync(
            "/api/specs/semantic-search",
            ApiClientJson.ToJsonContent(new
            {
                queries = new[] { " ", "" },
                topK = 5,
                minScore = 0.5
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(400);
        json.Message.Should().Be("请至少输入一条搜索内容");
    }

    private async Task<(int CustomerId, int ProcessId, int Spec1Id, int Spec2Id, string Query1, string Query2)> SeedSemanticSearchSpecsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var customer = new Customer
        {
            Name = $"语义客户-{suffix}",
            CreatedAt = DateTime.Now
        };
        var process = new Process
        {
            Name = $"语义制程-{suffix}",
            CreatedAt = DateTime.Now
        };
        var wordFile = new WordFile
        {
            FileName = $"semantic-{suffix}.docx",
            FileContent = Array.Empty<byte>(),
            FileHash = Guid.NewGuid().ToString("N"),
            UploadedAt = DateTime.Now
        };

        dbContext.Customers.Add(customer);
        dbContext.Processes.Add(process);
        dbContext.WordFiles.Add(wordFile);
        await dbContext.SaveChangesAsync();

        var spec1 = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            Project = "平台吸附精度",
            Specification = "真空吸附平台平面度需控制在0.05mm以内",
            Acceptance = "使用塞尺确认平台精度",
            Remark = "首件确认",
            WordFileId = wordFile.Id,
            OwnerOrgUnitId = 1,
            CreatedByUserId = 1,
            ImportedAt = DateTime.Now
        };
        var spec2 = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            Project = "升降模组行程",
            Specification = "升降模组有效行程需达到300mm",
            Acceptance = "满行程测试通过",
            Remark = "量产机种",
            WordFileId = wordFile.Id,
            OwnerOrgUnitId = 1,
            CreatedByUserId = 1,
            ImportedAt = DateTime.Now.AddMinutes(1)
        };
        var spec3 = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            Project = "视觉识别能力",
            Specification = "相机需识别Mark点并回传补偿坐标",
            Acceptance = "连续运行30次无漏检",
            Remark = "调试阶段",
            WordFileId = wordFile.Id,
            OwnerOrgUnitId = 1,
            CreatedByUserId = 1,
            ImportedAt = DateTime.Now.AddMinutes(2)
        };

        dbContext.AcceptanceSpecs.AddRange(spec1, spec2, spec3);
        await dbContext.SaveChangesAsync();

        return (
            customer.Id,
            process.Id,
            spec1.Id,
            spec2.Id,
            BuildQuery(spec1),
            BuildQuery(spec2));
    }

    private async Task<(int CustomerId, int ProcessId, int InScopeSpecId, string Query)> SeedScopedSemanticSearchSpecsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var customer = new Customer
        {
            Name = $"语义范围客户-{suffix}",
            CreatedAt = DateTime.Now
        };
        var process = new Process
        {
            Name = $"语义范围制程-{suffix}",
            CreatedAt = DateTime.Now
        };
        var wordFile = new WordFile
        {
            FileName = $"semantic-scope-{suffix}.docx",
            FileContent = Array.Empty<byte>(),
            FileHash = Guid.NewGuid().ToString("N"),
            UploadedAt = DateTime.Now
        };
        var inScopeOrg = new OrgUnit
        {
            CompanyId = 1,
            ParentId = 1,
            UnitType = OrgUnitType.Division,
            Code = $"SEM-IN-{suffix}",
            Name = $"语义范围内组织-{suffix}",
            Path = "/1/",
            Depth = 1,
            Sort = 1,
            IsActive = true,
            CreatedAt = DateTime.Now
        };
        var outOfScopeOrg = new OrgUnit
        {
            CompanyId = 1,
            ParentId = 1,
            UnitType = OrgUnitType.Division,
            Code = $"SEM-OUT-{suffix}",
            Name = $"语义范围外组织-{suffix}",
            Path = "/1/",
            Depth = 1,
            Sort = 2,
            IsActive = true,
            CreatedAt = DateTime.Now
        };

        dbContext.Customers.Add(customer);
        dbContext.Processes.Add(process);
        dbContext.WordFiles.Add(wordFile);
        dbContext.OrgUnits.AddRange(inScopeOrg, outOfScopeOrg);
        await dbContext.SaveChangesAsync();

        inScopeOrg.Path = $"/1/{inScopeOrg.Id}/";
        outOfScopeOrg.Path = $"/1/{outOfScopeOrg.Id}/";

        await ConfigureCommonSpecScopeAsync(dbContext, inScopeOrg.Id);

        var inScopeSpec = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            Project = "范围内语义项目",
            Specification = "机械手夹爪重复定位精度需小于0.02mm",
            Acceptance = "使用量表重复测试",
            Remark = "范围内",
            WordFileId = wordFile.Id,
            OwnerOrgUnitId = inScopeOrg.Id,
            CreatedByUserId = 1,
            ImportedAt = DateTime.Now
        };
        var outOfScopeSpec = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            Project = inScopeSpec.Project,
            Specification = inScopeSpec.Specification,
            Acceptance = inScopeSpec.Acceptance,
            Remark = "范围外",
            WordFileId = wordFile.Id,
            OwnerOrgUnitId = outOfScopeOrg.Id,
            CreatedByUserId = 1,
            ImportedAt = DateTime.Now.AddMinutes(1)
        };

        dbContext.AcceptanceSpecs.AddRange(inScopeSpec, outOfScopeSpec);
        await dbContext.SaveChangesAsync();

        return (customer.Id, process.Id, inScopeSpec.Id, BuildQuery(inScopeSpec));
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
                CreatedAt = DateTime.Now
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

    private static string BuildQuery(AcceptanceSpec spec)
    {
        return string.Join(
            "\n",
            new[]
            {
                spec.Project,
                spec.Specification,
                spec.Acceptance,
                spec.Remark
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
