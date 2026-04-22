using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Reflection;

namespace AcceptanceSpecSystem.Data.Tests;

/// <summary>
/// UnitOfWork测试
/// </summary>
public class UnitOfWorkTests
{
    private static (AppDbContext context, IServiceProvider serviceProvider) CreateContextWithServices()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IProcessRepository, ProcessRepository>();
        services.AddScoped<IMachineModelRepository, MachineModelRepository>();
        services.AddScoped<IAcceptanceSpecRepository, AcceptanceSpecRepository>();
        services.AddScoped<IEmbeddingCacheRepository, EmbeddingCacheRepository>();
        services.AddScoped<IWordFileRepository, WordFileRepository>();
        services.AddScoped<IAiServiceConfigRepository, AiServiceConfigRepository>();
        services.AddScoped<IPromptTemplateRepository, PromptTemplateRepository>();
        services.AddScoped<IColumnMappingRuleRepository, ColumnMappingRuleRepository>();
        services.AddScoped<ISystemUserRepository, SystemUserRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IMatchingFillTaskRepository, MatchingFillTaskRepository>();

        var serviceProvider = services.BuildServiceProvider();
        return (context, serviceProvider);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldPersistChanges()
    {
        var (context, serviceProvider) = CreateContextWithServices();
        using var unitOfWork = new UnitOfWork(context, serviceProvider);
        await unitOfWork.Customers.AddAsync(new Customer { Name = "测试客户" });

        var result = await unitOfWork.SaveChangesAsync();

        result.Should().Be(1);
        var customers = await unitOfWork.Customers.GetAllAsync();
        customers.Should().HaveCount(1);
    }

    [Fact(Skip = "InMemory数据库不支持事务，此测试需要使用SQLite进行集成测试")]
    public async Task Transaction_ShouldCommit_WhenSuccessful()
    {
        var (context, serviceProvider) = CreateContextWithServices();
        using var unitOfWork = new UnitOfWork(context, serviceProvider);

        await unitOfWork.BeginTransactionAsync();
        await unitOfWork.Customers.AddAsync(new Customer { Name = "客户1" });
        await unitOfWork.Customers.AddAsync(new Customer { Name = "客户2" });
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.CommitTransactionAsync();

        var customers = await unitOfWork.Customers.GetAllAsync();
        customers.Should().HaveCount(2);
    }

    [Fact(Skip = "InMemory数据库不支持事务，此测试需要使用SQLite进行集成测试")]
    public async Task Transaction_ShouldRollback_WhenCalled()
    {
        var (context, serviceProvider) = CreateContextWithServices();
        using var unitOfWork = new UnitOfWork(context, serviceProvider);
        await unitOfWork.Customers.AddAsync(new Customer { Name = "已存在客户" });
        await unitOfWork.SaveChangesAsync();

        await unitOfWork.BeginTransactionAsync();
        await unitOfWork.Customers.AddAsync(new Customer { Name = "新客户" });
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.RollbackTransactionAsync();

        var customers = await unitOfWork.Customers.GetAllAsync();
        customers.Should().NotBeEmpty();
    }

    [Fact]
    public void Repositories_ShouldBeLazilyInitialized()
    {
        var (context, serviceProvider) = CreateContextWithServices();
        using var unitOfWork = new UnitOfWork(context, serviceProvider);

        unitOfWork.Customers.Should().NotBeNull();
        unitOfWork.Processes.Should().NotBeNull();
        unitOfWork.AcceptanceSpecs.Should().NotBeNull();
        unitOfWork.EmbeddingCaches.Should().NotBeNull();
        unitOfWork.WordFiles.Should().NotBeNull();
        unitOfWork.AiServiceConfigs.Should().NotBeNull();
        unitOfWork.PromptTemplates.Should().NotBeNull();
        unitOfWork.ColumnMappingRules.Should().NotBeNull();
    }

    [Fact]
    public async Task MultipleRepositories_ShouldShareContext()
    {
        var (context, serviceProvider) = CreateContextWithServices();
        using var unitOfWork = new UnitOfWork(context, serviceProvider);
        var customer = new Customer { Name = "共享上下文测试" };
        await unitOfWork.Customers.AddAsync(customer);
        await unitOfWork.SaveChangesAsync();

        var process = new Process { Name = "制程" };
        await unitOfWork.Processes.AddAsync(process);
        await unitOfWork.SaveChangesAsync();

        var savedProcess = await unitOfWork.Processes.GetByIdAsync(process.Id);
        savedProcess.Should().NotBeNull();
        savedProcess!.Name.Should().Be("制程");
    }

    [Fact]
    public async Task RollbackTransactionAsync_ShouldClearTransaction_WhenRollbackThrows()
    {
        var (context, serviceProvider) = CreateContextWithServices();
        using var unitOfWork = new UnitOfWork(context, serviceProvider);
        var transaction = new ThrowingRollbackDbContextTransaction();
        SetTransaction(unitOfWork, transaction);

        await unitOfWork.Invoking(item => item.RollbackTransactionAsync()).Should().NotThrowAsync();

        transaction.DisposeAsyncCalled.Should().BeTrue();
        GetTransaction(unitOfWork).Should().BeNull();
    }

    private static void SetTransaction(UnitOfWork unitOfWork, IDbContextTransaction transaction)
    {
        var field = typeof(UnitOfWork).GetField("_transaction", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(unitOfWork, transaction);
    }

    private static IDbContextTransaction? GetTransaction(UnitOfWork unitOfWork)
    {
        var field = typeof(UnitOfWork).GetField("_transaction", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(unitOfWork) as IDbContextTransaction;
    }

    private sealed class ThrowingRollbackDbContextTransaction : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();

        public bool SupportsSavepoints => false;

        public bool DisposeAsyncCalled { get; private set; }

        public void Commit()
        {
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Rollback()
        {
            throw new InvalidOperationException("模拟连接已关闭");
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("模拟连接已关闭");
        }

        public void CreateSavepoint(string name)
        {
            throw new NotSupportedException();
        }

        public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void RollbackToSavepoint(string name)
        {
            throw new NotSupportedException();
        }

        public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void ReleaseSavepoint(string name)
        {
            throw new NotSupportedException();
        }

        public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            DisposeAsyncCalled = true;
            return ValueTask.CompletedTask;
        }
    }
}
