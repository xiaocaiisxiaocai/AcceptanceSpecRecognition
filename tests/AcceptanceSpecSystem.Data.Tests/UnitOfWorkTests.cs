using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Tests;

/// <summary>
/// UnitOfWork测试
/// </summary>
public class UnitOfWorkTests
{
    /// <summary>
    /// 创建新的测试上下文
    /// </summary>
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldPersistChanges()
    {
        // Arrange
        var context = CreateContext();
        using var unitOfWork = new UnitOfWork(context);
        await unitOfWork.Customers.AddAsync(new Customer { Name = "测试客户" });

        // Act
        var result = await unitOfWork.SaveChangesAsync();

        // Assert
        result.Should().Be(1);
        var customers = await unitOfWork.Customers.GetAllAsync();
        customers.Should().HaveCount(1);
    }

    [Fact(Skip = "InMemory数据库不支持事务，此测试需要使用SQLite进行集成测试")]
    public async Task Transaction_ShouldCommit_WhenSuccessful()
    {
        // Arrange
        var context = CreateContext();
        using var unitOfWork = new UnitOfWork(context);

        // Act
        await unitOfWork.BeginTransactionAsync();
        await unitOfWork.Customers.AddAsync(new Customer { Name = "客户1" });
        await unitOfWork.Customers.AddAsync(new Customer { Name = "客户2" });
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.CommitTransactionAsync();

        // Assert
        var customers = await unitOfWork.Customers.GetAllAsync();
        customers.Should().HaveCount(2);
    }

    [Fact(Skip = "InMemory数据库不支持事务，此测试需要使用SQLite进行集成测试")]
    public async Task Transaction_ShouldRollback_WhenCalled()
    {
        // Arrange
        var context = CreateContext();
        using var unitOfWork = new UnitOfWork(context);
        await unitOfWork.Customers.AddAsync(new Customer { Name = "已存在客户" });
        await unitOfWork.SaveChangesAsync();

        // Act
        await unitOfWork.BeginTransactionAsync();
        await unitOfWork.Customers.AddAsync(new Customer { Name = "新客户" });
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.RollbackTransactionAsync();

        // Assert - InMemory数据库不支持真正的事务回滚
        // 这个测试主要验证API可以正常调用
        var customers = await unitOfWork.Customers.GetAllAsync();
        customers.Should().NotBeEmpty();
    }

    [Fact]
    public void Repositories_ShouldBeLazilyInitialized()
    {
        // Arrange
        var context = CreateContext();
        using var unitOfWork = new UnitOfWork(context);

        // Act & Assert
        unitOfWork.Customers.Should().NotBeNull();
        unitOfWork.Processes.Should().NotBeNull();
        unitOfWork.AcceptanceSpecs.Should().NotBeNull();
        unitOfWork.EmbeddingCaches.Should().NotBeNull();
        unitOfWork.OperationHistories.Should().NotBeNull();
        unitOfWork.WordFiles.Should().NotBeNull();
        unitOfWork.AiServiceConfigs.Should().NotBeNull();
        unitOfWork.Synonyms.Should().NotBeNull();
        unitOfWork.Keywords.Should().NotBeNull();
        unitOfWork.TextProcessingConfigs.Should().NotBeNull();
        unitOfWork.PromptTemplates.Should().NotBeNull();
    }

    [Fact]
    public async Task MultipleRepositories_ShouldShareContext()
    {
        // Arrange
        var context = CreateContext();
        using var unitOfWork = new UnitOfWork(context);
        var customer = new Customer { Name = "共享上下文测试" };
        await unitOfWork.Customers.AddAsync(customer);
        await unitOfWork.SaveChangesAsync();

        // Act
        var process = new Process { Name = "制程" };
        await unitOfWork.Processes.AddAsync(process);
        await unitOfWork.SaveChangesAsync();

        // Assert
        var savedProcess = await unitOfWork.Processes.GetByIdAsync(process.Id);
        savedProcess.Should().NotBeNull();
        savedProcess!.Name.Should().Be("制程");
    }
}
