using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class SpecDataScopeTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SpecDataScopeTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task MatchingPreview_ShouldOnlyReturnScopedSpecs()
    {
        var (customerId, processId, inScopeSpecId, _) = await SeedScopedSpecsAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matching/preview");
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-Permissions", "*:*:*");
        request.Content = ApiClientJson.ToJsonContent(new
        {
            items = new[] { new { rowIndex = 1, project = "范围项目", specification = "范围规格" } },
            customerId,
            processId,
            config = new { minScoreThreshold = 0.0 }
        });

        using var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        json.Data.GetProperty("items")[0]
            .GetProperty("bestMatch")
            .GetProperty("specId")
            .GetInt32()
            .Should()
            .Be(inScopeSpecId);
    }

    [Fact]
    public async Task MatchingExecute_ShouldSkipOutOfScopeSpec()
    {
        var (_, _, _, outOfScopeSpecId) = await SeedScopedSpecsAsync();
        var fileId = await UploadWordFileAsync(
            "scope-execute.docx",
            CreateSingleTableDocxBytes("项目", "规格", "验收", "备注", "范围项目", "范围规格", "", ""));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matching/execute");
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-Permissions", "*:*:*");
        request.Content = ApiClientJson.ToJsonContent(new
        {
            fileId,
            tableIndex = 0,
            acceptanceColumnIndex = 2,
            remarkColumnIndex = 3,
            mappings = new[]
            {
                new
                {
                    rowIndex = 1,
                    specId = outOfScopeSpecId,
                    matchScore = 1.0
                }
            },
            highConfidenceThreshold = 0.95
        });

        using var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        json.Data.GetProperty("filledCount").GetInt32().Should().Be(0);
        json.Data.GetProperty("skippedCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetProcessSpecs_ShouldOnlyReturnScopedSpecs()
    {
        var (_, processId, inScopeSpecId, _) = await SeedScopedSpecsAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/processes/{processId}/specs");
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-Permissions", "*:*:*");

        using var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        var items = json.Data.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("id").GetInt32().Should().Be(inScopeSpecId);
    }

    [Fact]
    public async Task GetProcesses_ShouldUseScopedSpecCount()
    {
        var (_, processId, _, _) = await SeedScopedSpecsAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/processes");
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-Permissions", "*:*:*");

        using var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        var process = json.Data.GetProperty("items")
            .EnumerateArray()
            .First(item => item.GetProperty("id").GetInt32() == processId);
        process.GetProperty("specCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetDuplicateGroups_ShouldOnlyReturnScopedExactAndSimilarGroups()
    {
        var (customerId, processId, outOfScopeSpecId) = await SeedDuplicateSpecsAsync();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/specs/duplicate-groups?customerId={customerId}&processId={processId}&machineModelIdIsNull=true");
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-Permissions", "*:*:*");

        using var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);

        var data = json.Data;
        data.GetProperty("exactGroupCount").GetInt32().Should().Be(1);
        data.GetProperty("similarGroupCount").GetInt32().Should().Be(1);

        var exactGroups = data.GetProperty("exactGroups");
        exactGroups.GetArrayLength().Should().Be(1);
        exactGroups[0].GetProperty("items").GetArrayLength().Should().Be(2);

        var similarGroups = data.GetProperty("similarGroups");
        similarGroups.GetArrayLength().Should().Be(1);
        similarGroups[0].GetProperty("items").GetArrayLength().Should().Be(2);

        var allReturnedIds = exactGroups[0].GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32())
            .Concat(
                similarGroups[0].GetProperty("items")
                    .EnumerateArray()
                    .Select(item => item.GetProperty("id").GetInt32()))
            .ToList();
        allReturnedIds.Should().NotContain(outOfScopeSpecId);
    }

    private async Task<(int CustomerId, int ProcessId, int InScopeSpecId, int OutOfScopeSpecId)> SeedScopedSpecsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await dbContext.AcceptanceSpecs.AnyAsync(s =>
                s.Acceptance == "范围内验收" || s.Acceptance == "范围外验收"))
        {
            var seededCustomerId = await dbContext.Customers
                .Where(c => c.Name == "范围客户")
                .Select(c => c.Id)
                .FirstAsync();
            var seededProcessId = await dbContext.Processes
                .Where(p => p.Name == "范围制程")
                .Select(p => p.Id)
                .FirstAsync();
            var inScopeSpec = await dbContext.AcceptanceSpecs
                .Where(s => s.Acceptance == "范围内验收")
                .Select(s => new { s.Id, s.OwnerOrgUnitId })
                .FirstAsync();
            var outScopeId = await dbContext.AcceptanceSpecs
                .Where(s => s.Acceptance == "范围外验收")
                .Select(s => s.Id)
                .FirstAsync();

            await ConfigureCommonSpecScopeAsync(dbContext, inScopeSpec.OwnerOrgUnitId!.Value);
            return (seededCustomerId, seededProcessId, inScopeSpec.Id, outScopeId);
        }

        var customer = new Customer
        {
            Name = "范围客户",
            CreatedAt = DateTime.Now
        };
        var process = new Process
        {
            Name = "范围制程",
            CreatedAt = DateTime.Now
        };
        var wordFile = new WordFile
        {
            FileName = "scope-source.docx",
            FileContent = Array.Empty<byte>(),
            FileHash = Guid.NewGuid().ToString("N"),
            UploadedAt = DateTime.Now
        };
        var inScopeOrg = new OrgUnit
        {
            CompanyId = 1,
            ParentId = 1,
            UnitType = OrgUnitType.Division,
            Code = "SCOPE-IN",
            Name = "范围内组织",
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
            Code = "SCOPE-OUT",
            Name = "范围外组织",
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

        var inScopeSpecEntity = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            Project = "范围项目",
            Specification = "范围规格",
            Acceptance = "范围内验收",
            Remark = "范围内备注",
            WordFileId = wordFile.Id,
            OwnerOrgUnitId = inScopeOrg.Id,
            CreatedByUserId = 1,
            ImportedAt = DateTime.Now
        };
        var outOfScopeSpec = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            Project = "范围项目",
            Specification = "范围规格",
            Acceptance = "范围外验收",
            Remark = "范围外备注",
            WordFileId = wordFile.Id,
            OwnerOrgUnitId = outOfScopeOrg.Id,
            CreatedByUserId = 1,
            ImportedAt = DateTime.Now.AddMinutes(1)
        };

        dbContext.AcceptanceSpecs.AddRange(inScopeSpecEntity, outOfScopeSpec);
        await dbContext.SaveChangesAsync();

        return (customer.Id, process.Id, inScopeSpecEntity.Id, outOfScopeSpec.Id);
    }

    private async Task<(int CustomerId, int ProcessId, int OutOfScopeSpecId)> SeedDuplicateSpecsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var customer = new Customer
        {
            Name = $"排查客户-{suffix}",
            CreatedAt = DateTime.Now
        };
        var process = new Process
        {
            Name = $"排查制程-{suffix}",
            CreatedAt = DateTime.Now
        };
        var wordFile = new WordFile
        {
            FileName = $"duplicate-{suffix}.docx",
            FileContent = Array.Empty<byte>(),
            FileHash = Guid.NewGuid().ToString("N"),
            UploadedAt = DateTime.Now
        };
        var inScopeOrg = new OrgUnit
        {
            CompanyId = 1,
            ParentId = 1,
            UnitType = OrgUnitType.Division,
            Code = $"DUP-IN-{suffix}",
            Name = $"排查范围内组织-{suffix}",
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
            Code = $"DUP-OUT-{suffix}",
            Name = $"排查范围外组织-{suffix}",
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

        var exactA = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            Project = "设备设计要求",
            Specification = "放板机生产载位对接AGV，离地最低处为360mm。",
            Acceptance = "完全重复A",
            WordFileId = wordFile.Id,
            OwnerOrgUnitId = inScopeOrg.Id,
            CreatedByUserId = 1,
            ImportedAt = DateTime.Now
        };
        var exactB = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            Project = "设备设计要求",
            Specification = "放板机生产载位对接AGV 离地最低处为360mm",
            Acceptance = "完全重复B",
            WordFileId = wordFile.Id,
            OwnerOrgUnitId = inScopeOrg.Id,
            CreatedByUserId = 1,
            ImportedAt = DateTime.Now.AddMinutes(1)
        };
        var similarA = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            Project = "设备设计要求",
            Specification = "设备PLC采用欧姆龙或三菱，收放板机单独设置感应器与AGV交互。",
            Acceptance = "近重复A",
            WordFileId = wordFile.Id,
            OwnerOrgUnitId = inScopeOrg.Id,
            CreatedByUserId = 1,
            ImportedAt = DateTime.Now.AddMinutes(2)
        };
        var similarB = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            Project = "设备设计要求",
            Specification = "设备PLC采用欧姆龙或三菱等品牌，收放板机单独设置感应器与AGV交互。",
            Acceptance = "近重复B",
            WordFileId = wordFile.Id,
            OwnerOrgUnitId = inScopeOrg.Id,
            CreatedByUserId = 1,
            ImportedAt = DateTime.Now.AddMinutes(3)
        };
        var outOfScope = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            Project = "设备设计要求",
            Specification = "放板机生产载位对接AGV，离地最低处为360mm。",
            Acceptance = "范围外重复",
            WordFileId = wordFile.Id,
            OwnerOrgUnitId = outOfScopeOrg.Id,
            CreatedByUserId = 1,
            ImportedAt = DateTime.Now.AddMinutes(4)
        };

        dbContext.AcceptanceSpecs.AddRange(exactA, exactB, similarA, similarB, outOfScope);
        await dbContext.SaveChangesAsync();

        return (customer.Id, process.Id, outOfScope.Id);
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

    private async Task<int> UploadWordFileAsync(string fileName, byte[] content)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(content), "file", fileName);

        using var response = await _client.PostAsync("/api/documents/upload", multipart);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateSingleTableDocxBytes(params string[] cells)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            var table = new Table();
            var body = mainPart.Document.Body!;

            for (var index = 0; index < cells.Length; index += 4)
            {
                var row = new TableRow();
                for (var offset = 0; offset < 4; offset++)
                {
                    row.AppendChild(new TableCell(
                        new Paragraph(
                            new Run(
                                new Text(cells[index + offset] ?? string.Empty)))));
                }

                table.AppendChild(row);
            }

            body.AppendChild(table);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }
}
