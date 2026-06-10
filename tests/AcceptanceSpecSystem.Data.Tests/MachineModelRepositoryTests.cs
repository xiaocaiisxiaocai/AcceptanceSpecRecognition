using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

/// <summary>
/// 机型 Repository 测试。
/// </summary>
public class MachineModelRepositoryTests : TestBase
{
    [Fact]
    public async Task GetWithSpecCountAsync_ShouldIncludeAcceptanceSpecs()
    {
        // Arrange
        var repository = new MachineModelRepository(Context);
        var customer = new Customer { Name = "客户" };
        var process = new Process { Name = "制程" };
        var machineModel = new MachineModel { Name = "机型" };
        var wordFile = new WordFile { FileName = "test.docx", FileHash = "hash" };
        Context.Customers.Add(customer);
        Context.Processes.Add(process);
        Context.MachineModels.Add(machineModel);
        Context.WordFiles.Add(wordFile);
        await Context.SaveChangesAsync();

        Context.AcceptanceSpecs.AddRange(
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                ProcessId = process.Id,
                MachineModelId = machineModel.Id,
                WordFileId = wordFile.Id,
                Project = "P1",
                Specification = "S1"
            },
            new AcceptanceSpec
            {
                CustomerId = customer.Id,
                ProcessId = process.Id,
                MachineModelId = machineModel.Id,
                WordFileId = wordFile.Id,
                Project = "P2",
                Specification = "S2"
            });
        await Context.SaveChangesAsync();

        // Act
        var result = await repository.GetWithSpecCountAsync(machineModel.Id);

        // Assert
        result.Should().NotBeNull();
        result!.AcceptanceSpecs.Should().HaveCount(2);
        result.AcceptanceSpecs.Select(spec => spec.Specification)
            .Should()
            .BeEquivalentTo(["S1", "S2"]);
    }

    [Fact]
    public async Task GetWithSpecCountAsync_ShouldReturnNull_WhenMachineModelDoesNotExist()
    {
        // Arrange
        var repository = new MachineModelRepository(Context);

        // Act
        var result = await repository.GetWithSpecCountAsync(404);

        // Assert
        result.Should().BeNull();
    }
}
