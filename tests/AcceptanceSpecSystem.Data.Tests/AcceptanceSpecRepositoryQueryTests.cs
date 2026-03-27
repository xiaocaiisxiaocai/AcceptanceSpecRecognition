using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

public class AcceptanceSpecRepositoryQueryTests : TestBase
{
    [Fact]
    public async Task GetPagedWithFilterAsync_ShouldApplyScopeKeywordAndPagination()
    {
        var repo = new AcceptanceSpecRepository(Context);
        var inScopeOrg = new OrgUnit
        {
            CompanyId = 1,
            ParentId = null,
            UnitType = OrgUnitType.Company,
            Code = "QUERY-IN",
            Name = "查询范围内",
            Path = "/1/",
            Depth = 0,
            Sort = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var outScopeOrg = new OrgUnit
        {
            CompanyId = 1,
            ParentId = null,
            UnitType = OrgUnitType.Company,
            Code = "QUERY-OUT",
            Name = "查询范围外",
            Path = "/2/",
            Depth = 0,
            Sort = 2,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var customer = new Customer { Name = "查询客户", CreatedAt = DateTime.UtcNow };
        var process = new Process { Name = "查询制程", CreatedAt = DateTime.UtcNow };
        var machine = new MachineModel { Name = "查询机型", CreatedAt = DateTime.UtcNow };
        var wordFile = new WordFile
        {
            FileName = "query.docx",
            FileHash = Guid.NewGuid().ToString("N"),
            FileContent = Array.Empty<byte>(),
            UploadedAt = DateTime.UtcNow
        };

        Context.OrgUnits.AddRange(inScopeOrg, outScopeOrg);
        Context.Customers.Add(customer);
        Context.Processes.Add(process);
        Context.MachineModels.Add(machine);
        Context.WordFiles.Add(wordFile);
        await Context.SaveChangesAsync();

        Context.AcceptanceSpecs.AddRange(
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                ProcessId = process.Id,
                MachineModelId = machine.Id,
                Project = "视觉检测-1",
                Specification = "尺寸检测",
                WordFileId = wordFile.Id,
                OwnerOrgUnitId = inScopeOrg.Id,
                CreatedByUserId = 7,
                ImportedAt = DateTime.UtcNow.AddMinutes(-2)
            },
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                ProcessId = process.Id,
                MachineModelId = machine.Id,
                Project = "视觉检测-2",
                Specification = "平面度检测",
                WordFileId = wordFile.Id,
                OwnerOrgUnitId = inScopeOrg.Id,
                CreatedByUserId = 7,
                ImportedAt = DateTime.UtcNow.AddMinutes(-1)
            },
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                ProcessId = process.Id,
                MachineModelId = machine.Id,
                Project = "视觉检测-3",
                Specification = "范围外数据",
                WordFileId = wordFile.Id,
                OwnerOrgUnitId = outScopeOrg.Id,
                CreatedByUserId = 99,
                ImportedAt = DateTime.UtcNow
            });
        await Context.SaveChangesAsync();

        var result = await repo.GetPagedWithFilterAsync(new AcceptanceSpecQueryOptions
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            MachineModelId = machine.Id,
            Keyword = "视觉",
            OrgUnitIds = [inScopeOrg.Id],
            Page = 2,
            PageSize = 1
        });

        result.Total.Should().Be(2);
        result.Items.Should().ContainSingle();
        result.Items[0].Project.Should().Be("视觉检测-1");
    }

    [Fact]
    public async Task GetGroupSummaryWithFilterAsync_ShouldIncludeSelfOwnedSpecs_WhenIncludeSelfEnabled()
    {
        var repo = new AcceptanceSpecRepository(Context);
        var scopedOrg = new OrgUnit
        {
            CompanyId = 1,
            ParentId = null,
            UnitType = OrgUnitType.Company,
            Code = "GROUP-IN",
            Name = "分组范围内",
            Path = "/3/",
            Depth = 0,
            Sort = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var otherOrg = new OrgUnit
        {
            CompanyId = 1,
            ParentId = null,
            UnitType = OrgUnitType.Company,
            Code = "GROUP-OUT",
            Name = "分组范围外",
            Path = "/4/",
            Depth = 0,
            Sort = 2,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var customer = new Customer { Name = "分组客户", CreatedAt = DateTime.UtcNow };
        var process = new Process { Name = "分组制程", CreatedAt = DateTime.UtcNow };
        var wordFile = new WordFile
        {
            FileName = "group.docx",
            FileHash = Guid.NewGuid().ToString("N"),
            FileContent = Array.Empty<byte>(),
            UploadedAt = DateTime.UtcNow
        };

        Context.OrgUnits.AddRange(scopedOrg, otherOrg);
        Context.Customers.Add(customer);
        Context.Processes.Add(process);
        Context.WordFiles.Add(wordFile);
        await Context.SaveChangesAsync();

        Context.AcceptanceSpecs.AddRange(
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                ProcessId = process.Id,
                Project = "分组项目-组织范围",
                Specification = "组织范围",
                WordFileId = wordFile.Id,
                OwnerOrgUnitId = scopedOrg.Id,
                CreatedByUserId = 8,
                ImportedAt = DateTime.UtcNow.AddMinutes(-2)
            },
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                ProcessId = process.Id,
                Project = "分组项目-本人范围",
                Specification = "本人范围",
                WordFileId = wordFile.Id,
                OwnerOrgUnitId = otherOrg.Id,
                CreatedByUserId = 42,
                ImportedAt = DateTime.UtcNow.AddMinutes(-1)
            },
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                ProcessId = process.Id,
                Project = "分组项目-无权限",
                Specification = "无权限",
                WordFileId = wordFile.Id,
                OwnerOrgUnitId = otherOrg.Id,
                CreatedByUserId = 99,
                ImportedAt = DateTime.UtcNow
            });
        await Context.SaveChangesAsync();

        var result = await repo.GetGroupSummaryWithFilterAsync(new AcceptanceSpecQueryOptions
        {
            UserId = 42,
            IncludeSelf = true,
            OrgUnitIds = [scopedOrg.Id]
        });

        result.Should().ContainSingle();
        result[0].SpecCount.Should().Be(2);
        result[0].CustomerId.Should().Be(customer.Id);
        result[0].ProcessId.Should().Be(process.Id);
    }
}
