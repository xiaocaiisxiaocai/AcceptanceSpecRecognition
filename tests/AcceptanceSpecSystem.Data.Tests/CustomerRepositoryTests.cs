using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

/// <summary>
/// 客户Repository测试
/// </summary>
public class CustomerRepositoryTests : TestBase
{
    private readonly CustomerRepository _repository;

    public CustomerRepositoryTests()
    {
        _repository = new CustomerRepository(Context);
    }

    [Fact]
    public async Task AddAsync_ShouldAddCustomer()
    {
        // Arrange
        var customer = new Customer { Name = "测试客户" };

        // Act
        await _repository.AddAsync(customer);
        await Context.SaveChangesAsync();

        // Assert
        var result = await _repository.GetByIdAsync(customer.Id);
        result.Should().NotBeNull();
        result!.Name.Should().Be("测试客户");
    }

    [Fact]
    public async Task GetByNameAsync_ShouldReturnCustomer_WhenExists()
    {
        // Arrange
        var customer = new Customer { Name = "客户A" };
        await _repository.AddAsync(customer);
        await Context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByNameAsync("客户A");

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("客户A");
    }

    [Fact]
    public async Task GetByNameAsync_ShouldReturnNull_WhenNotExists()
    {
        // Act
        var result = await _repository.GetByNameAsync("不存在的客户");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnAllCustomers()
    {
        // Arrange
        await _repository.AddAsync(new Customer { Name = "客户1" });
        await _repository.AddAsync(new Customer { Name = "客户2" });
        await _repository.AddAsync(new Customer { Name = "客户3" });
        await Context.SaveChangesAsync();

        // Act
        var result = await _repository.GetAllAsync();

        // Assert
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task Update_ShouldUpdateCustomer()
    {
        // Arrange
        var customer = new Customer { Name = "原名称" };
        await _repository.AddAsync(customer);
        await Context.SaveChangesAsync();

        // Act
        customer.Name = "新名称";
        _repository.Update(customer);
        await Context.SaveChangesAsync();

        // Assert
        var result = await _repository.GetByIdAsync(customer.Id);
        result!.Name.Should().Be("新名称");
    }

    [Fact]
    public async Task Remove_ShouldDeleteCustomer()
    {
        // Arrange
        var customer = new Customer { Name = "待删除" };
        await _repository.AddAsync(customer);
        await Context.SaveChangesAsync();

        // Act
        _repository.Remove(customer);
        await Context.SaveChangesAsync();

        // Assert
        var result = await _repository.GetByIdAsync(customer.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetWithAcceptanceSpecsAsync_ShouldIncludeAcceptanceSpecs()
    {
        // Arrange
        var customer = new Customer { Name = "客户" };
        var process = new Process { Name = "制程" };
        var wordFile = new WordFile { FileName = "test.docx", FileHash = "hash" };
        Context.Processes.Add(process);
        Context.WordFiles.Add(wordFile);
        await _repository.AddAsync(customer);
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
        var result = await _repository.GetWithAcceptanceSpecsAsync(customer.Id);

        // Assert
        result.Should().NotBeNull();
        result!.AcceptanceSpecs.Should().HaveCount(1);
    }
}
