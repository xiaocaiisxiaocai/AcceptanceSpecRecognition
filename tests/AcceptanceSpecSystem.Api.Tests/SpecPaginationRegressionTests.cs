using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class SpecPaginationRegressionTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SpecPaginationRegressionTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetSpecs_PageSize1000_ShouldNotBeTrimmedTo200()
    {
        var (customerId, processId, expectedCount) = await SeedSpecsAsync(250);

        var response = await _client.GetAsync(
            $"/api/specs?page=1&pageSize=1000&customerId={customerId}&processId={processId}");

        response.EnsureSuccessStatusCode();

        var payload = await response.ReadAsAsync<ApiResponse<PagedData<JsonElement>>>();
        payload.Code.Should().Be(0);
        payload.Data.Should().NotBeNull();
        payload.Data!.PageSize.Should().Be(1000);
        payload.Data.Total.Should().Be(expectedCount);
        payload.Data.Items.Should().HaveCount(expectedCount);
    }

    [Fact]
    public async Task GetSpecs_GlobalKeyword_ShouldIgnoreCustomerMachineModelAndProcessNames()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var keyword = $"全局对象-{suffix}";

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer
            {
                Name = $"客户-{keyword}",
                CreatedAt = DateTime.UtcNow
            };
            var process = new Process
            {
                Name = $"制程-{keyword}",
                CreatedAt = DateTime.UtcNow
            };
            var machineModel = new MachineModel
            {
                Name = $"机型-{keyword}",
                CreatedAt = DateTime.UtcNow
            };
            var wordFile = new WordFile
            {
                FileName = $"global-keyword-{suffix}.docx",
                FileHash = Guid.NewGuid().ToString("N"),
                FileContent = Array.Empty<byte>(),
                UploadedAt = DateTime.UtcNow
            };

            dbContext.Customers.Add(customer);
            dbContext.Processes.Add(process);
            dbContext.MachineModels.Add(machineModel);
            dbContext.WordFiles.Add(wordFile);
            await dbContext.SaveChangesAsync();

            dbContext.AcceptanceSpecs.Add(new AcceptanceSpec
            {
                CustomerId = customer.Id,
                ProcessId = process.Id,
                MachineModelId = machineModel.Id,
                Project = $"项目不含目标词-{suffix}",
                Specification = $"规格不含目标词-{suffix}",
                Acceptance = $"验收不含目标词-{suffix}",
                Remark = $"备注不含目标词-{suffix}",
                WordFileId = wordFile.Id,
                OwnerOrgUnitId = 1,
                CreatedByUserId = 1,
                ImportedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var response = await _client.GetAsync(
            $"/api/specs?page=1&pageSize=20&keyword={Uri.EscapeDataString(keyword)}");

        response.EnsureSuccessStatusCode();

        var payload = await response.ReadAsAsync<ApiResponse<PagedData<JsonElement>>>();
        payload.Code.Should().Be(0);
        payload.Data.Should().NotBeNull();
        payload.Data!.Total.Should().Be(0);
        payload.Data.Items.Should().BeEmpty();
    }

    private async Task<(int CustomerId, int ProcessId, int Count)> SeedSpecsAsync(int count)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var customer = new Customer
        {
            Name = $"分页客户-{suffix}",
            CreatedAt = DateTime.UtcNow
        };
        var process = new Process
        {
            Name = $"分页制程-{suffix}",
            CreatedAt = DateTime.UtcNow
        };
        var wordFile = new WordFile
        {
            FileName = $"spec-page-{suffix}.docx",
            FileHash = Guid.NewGuid().ToString("N"),
            FileContent = Array.Empty<byte>(),
            UploadedAt = DateTime.UtcNow
        };

        dbContext.Customers.Add(customer);
        dbContext.Processes.Add(process);
        dbContext.WordFiles.Add(wordFile);
        await dbContext.SaveChangesAsync();

        dbContext.AcceptanceSpecs.AddRange(Enumerable.Range(1, count).Select(index => new AcceptanceSpec
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            Project = $"分页项目-{index}",
            Specification = $"分页规格-{index}",
            Acceptance = $"分页验收-{index}",
            Remark = $"分页备注-{index}",
            WordFileId = wordFile.Id,
            OwnerOrgUnitId = 1,
            CreatedByUserId = 1,
            ImportedAt = DateTime.UtcNow.AddSeconds(index)
        }));
        await dbContext.SaveChangesAsync();

        return (customer.Id, process.Id, count);
    }
}
