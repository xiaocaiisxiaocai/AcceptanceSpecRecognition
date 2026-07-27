using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

public class AcceptanceSpecRepositoryQueryTests : TestBase
{
    [Fact]
    public async Task 重复分析候选查询应在数据库侧稳定截取上限加一并排除空白项()
    {
        var repo = new AcceptanceSpecRepository(Context);
        var customer = new Customer { Name = "重复分析候选客户", CreatedAt = DateTime.UtcNow };
        var wordFile = new WordFile
        {
            CompanyId = 7,
            FileName = "duplicate-candidates.docx",
            FileHash = Guid.NewGuid().ToString("N"),
            FileContent = [],
            UploadedAt = DateTime.UtcNow
        };
        Context.Customers.Add(customer);
        Context.WordFiles.Add(wordFile);
        await Context.SaveChangesAsync();

        Context.AcceptanceSpecs.AddRange(Enumerable.Range(1, 2_005).Select(index => new AcceptanceSpec
        {
            CustomerId = customer.Id,
            Project = index <= 2_003 ? $"项目-{index:D4}" : " ",
            Specification = index == 2_005 ? "\t" : $"规格-{index:D4}",
            WordFileId = wordFile.Id,
            ImportedAt = DateTime.UtcNow.AddSeconds(index)
        }));
        await Context.SaveChangesAsync();

        var result = await repo.GetDuplicateCandidatesAsync(
            new AcceptanceSpecQueryOptions
            {
                CompanyId = 7,
                IsAll = true,
                CustomerId = customer.Id
            },
            2_001);

        result.Should().HaveCount(2_001);
        result.Select(item => item.Id).Should().BeInAscendingOrder();
        result.Should().OnlyContain(item =>
            !string.IsNullOrWhiteSpace(item.Project) &&
            !string.IsNullOrWhiteSpace(item.Specification));
    }

    [Fact]
    public async Task 重复分析候选查询在无数据范围时应先返回空而不是触发数量预算()
    {
        var repo = new AcceptanceSpecRepository(Context);
        var customer = new Customer { Name = "无范围重复分析客户", CreatedAt = DateTime.UtcNow };
        var wordFile = new WordFile
        {
            CompanyId = 8,
            FileName = "no-scope.docx",
            FileHash = Guid.NewGuid().ToString("N"),
            FileContent = [],
            UploadedAt = DateTime.UtcNow
        };
        Context.Customers.Add(customer);
        Context.WordFiles.Add(wordFile);
        await Context.SaveChangesAsync();

        Context.AcceptanceSpecs.AddRange(Enumerable.Range(1, 2_002).Select(index => new AcceptanceSpec
        {
            CustomerId = customer.Id,
            Project = $"无范围项目-{index:D4}",
            Specification = $"无范围规格-{index:D4}",
            WordFileId = wordFile.Id,
            ImportedAt = DateTime.UtcNow
        }));
        await Context.SaveChangesAsync();

        var result = await repo.GetDuplicateCandidatesAsync(
            new AcceptanceSpecQueryOptions
            {
                CompanyId = 8,
                UserId = 99,
                IsAll = false,
                IncludeSelf = false,
                OrgUnitIds = []
            },
            2_001);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPagedWithFilterAsync_ShouldUseIdAsStableDescendingTieBreaker()
    {
        var repo = new AcceptanceSpecRepository(Context);
        var customer = new Customer
        {
            Name = "稳定排序客户",
            CreatedAt = DateTime.UtcNow
        };
        var wordFile = new WordFile
        {
            FileName = "stable-order.docx",
            FileHash = Guid.NewGuid().ToString("N"),
            FileContent = Array.Empty<byte>(),
            UploadedAt = DateTime.UtcNow
        };
        Context.Customers.Add(customer);
        Context.WordFiles.Add(wordFile);
        await Context.SaveChangesAsync();

        var importedAt = DateTime.UtcNow;
        Context.AcceptanceSpecs.AddRange(
            new AcceptanceSpec
            {
                Id = 100,
                CustomerId = customer.Id,
                Project = "较早主键",
                Specification = "相同导入时间",
                WordFileId = wordFile.Id,
                ImportedAt = importedAt
            },
            new AcceptanceSpec
            {
                Id = 200,
                CustomerId = customer.Id,
                Project = "较晚主键",
                Specification = "相同导入时间",
                WordFileId = wordFile.Id,
                ImportedAt = importedAt
            });
        await Context.SaveChangesAsync();

        var result = await repo.GetPagedWithFilterAsync(new AcceptanceSpecQueryOptions
        {
            IsAll = true,
            CustomerId = customer.Id,
            Page = 1,
            PageSize = 20
        });

        result.Items.Select(item => item.Id).Should().Equal(200, 100);
    }

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
    public async Task GetPagedWithFilterAsync_ShouldApplyImportedRange()
    {
        var repo = new AcceptanceSpecRepository(Context);
        var scopedOrg = new OrgUnit
        {
            CompanyId = 1,
            ParentId = null,
            UnitType = OrgUnitType.Company,
            Code = "DATE-IN",
            Name = "时间范围组织",
            Path = "/5/",
            Depth = 0,
            Sort = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var customer = new Customer { Name = "时间范围客户", CreatedAt = DateTime.UtcNow };
        var process = new Process { Name = "时间范围制程", CreatedAt = DateTime.UtcNow };
        var machine = new MachineModel { Name = "时间范围机型", CreatedAt = DateTime.UtcNow };
        var wordFile = new WordFile
        {
            FileName = "date-range.docx",
            FileHash = Guid.NewGuid().ToString("N"),
            FileContent = Array.Empty<byte>(),
            UploadedAt = DateTime.UtcNow
        };

        Context.OrgUnits.Add(scopedOrg);
        Context.Customers.Add(customer);
        Context.Processes.Add(process);
        Context.MachineModels.Add(machine);
        Context.WordFiles.Add(wordFile);
        await Context.SaveChangesAsync();

        var from = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 3, 20, 23, 59, 59, DateTimeKind.Utc);

        Context.AcceptanceSpecs.AddRange(
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                ProcessId = process.Id,
                MachineModelId = machine.Id,
                Project = "范围内-1",
                Specification = "尺寸 A",
                WordFileId = wordFile.Id,
                OwnerOrgUnitId = scopedOrg.Id,
                CreatedByUserId = 7,
                ImportedAt = new DateTime(2026, 3, 12, 8, 0, 0, DateTimeKind.Utc)
            },
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                ProcessId = process.Id,
                MachineModelId = machine.Id,
                Project = "范围内-2",
                Specification = "尺寸 B",
                WordFileId = wordFile.Id,
                OwnerOrgUnitId = scopedOrg.Id,
                CreatedByUserId = 7,
                ImportedAt = new DateTime(2026, 3, 19, 18, 0, 0, DateTimeKind.Utc)
            },
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                ProcessId = process.Id,
                MachineModelId = machine.Id,
                Project = "范围外",
                Specification = "尺寸 C",
                WordFileId = wordFile.Id,
                OwnerOrgUnitId = scopedOrg.Id,
                CreatedByUserId = 7,
                ImportedAt = new DateTime(2026, 3, 25, 8, 0, 0, DateTimeKind.Utc)
            });
        await Context.SaveChangesAsync();

        var result = await repo.GetPagedWithFilterAsync(new AcceptanceSpecQueryOptions
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            MachineModelId = machine.Id,
            OrgUnitIds = [scopedOrg.Id],
            ImportedFrom = from,
            ImportedTo = to,
            Page = 1,
            PageSize = 20
        });

        result.Total.Should().Be(2);
        result.Items.Select(item => item.Project).Should().BeEquivalentTo(["范围内-2", "范围内-1"]);
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
