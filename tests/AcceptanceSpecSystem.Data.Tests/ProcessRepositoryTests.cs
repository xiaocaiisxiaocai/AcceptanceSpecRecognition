using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

/// <summary>
/// 制程Repository测试
/// </summary>
public class ProcessRepositoryTests : TestBase
{
    private readonly ProcessRepository _repository;

    public ProcessRepositoryTests()
    {
        _repository = new ProcessRepository(Context);
    }

    [Fact]
    public async Task GetWithSpecCountAsync_ShouldIncludeAcceptanceSpecs()
    {
        // Arrange
        var customer = new Customer { Name = "客户" };
        var process = new Process { Name = "制程" };
        var wordFile = new WordFile { FileName = "test.docx", FileHash = "hash" };
        Context.Customers.Add(customer);
        Context.Processes.Add(process);
        Context.WordFiles.Add(wordFile);
        await Context.SaveChangesAsync();

        Context.AcceptanceSpecs.Add(new AcceptanceSpec
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            WordFileId = wordFile.Id,
            Project = "P",
            Specification = "S"
        });
        await Context.SaveChangesAsync();

        // Act
        var result = await _repository.GetWithSpecCountAsync(process.Id);

        // Assert
        result.Should().NotBeNull();
        result!.AcceptanceSpecs.Should().HaveCount(1);
    }
}
