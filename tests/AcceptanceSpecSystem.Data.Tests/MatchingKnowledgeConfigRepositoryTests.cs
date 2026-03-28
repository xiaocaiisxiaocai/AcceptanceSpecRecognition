using AcceptanceSpecSystem.Data.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Tests;

/// <summary>
/// 匹配知识配置仓储测试
/// </summary>
public class MatchingKnowledgeConfigRepositoryTests : IDisposable
{
    private readonly AppDbContext _context;

    public MatchingKnowledgeConfigRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task SaveConfigAsync_ShouldCreateSingletonConfig_WhenDatabaseIsEmpty()
    {
        // Arrange
        var repository = CreateRepository();
        var config = CreateConfig(
            entityAliasesJson: "{\"panasonic\":\"松下\"}",
            unitAliasesJson: "{\"厘米\":\"cm\"}",
            unitFactorsJson: "{\"cm\":10}",
            fieldAliasesJson: "{\"width\":\"宽度\"}",
            conflictPairsJson: "[[\"输入\",\"输出\"]]");

        // Act
        await InvokeSaveAsync(repository, config);
        await _context.SaveChangesAsync();

        // Assert
        var savedConfig = await InvokeGetAsync(repository);
        savedConfig.Should().NotBeNull();
        ReadStringProperty(savedConfig!, "EntityAliasesJson").Should().Be("{\"panasonic\":\"松下\"}");
        ReadStringProperty(savedConfig, "UnitAliasesJson").Should().Be("{\"厘米\":\"cm\"}");
        ReadStringProperty(savedConfig, "UnitFactorsJson").Should().Be("{\"cm\":10}");
        ReadStringProperty(savedConfig, "FieldAliasesJson").Should().Be("{\"width\":\"宽度\"}");
        ReadStringProperty(savedConfig, "ConflictPairsJson").Should().Be("[[\"输入\",\"输出\"]]");

        CountConfigs().Should().Be(1);
    }

    [Fact]
    public async Task SaveConfigAsync_ShouldUpdateExistingSingletonConfig_WhenConfigAlreadyExists()
    {
        // Arrange
        var repository = CreateRepository();
        await InvokeSaveAsync(repository, CreateConfig(
            entityAliasesJson: "{\"delta\":\"台达\"}",
            unitAliasesJson: "{\"毫米\":\"mm\"}",
            unitFactorsJson: "{\"mm\":1}",
            fieldAliasesJson: "{\"voltage\":\"电压\"}",
            conflictPairsJson: "[[\"loader\",\"unloader\"]]"));
        await _context.SaveChangesAsync();

        var updatedConfig = CreateConfig(
            entityAliasesJson: "{\"foxconn\":\"富士康\"}",
            unitAliasesJson: "{\"伏\":\"v\"}",
            unitFactorsJson: "{\"v\":1}",
            fieldAliasesJson: "{\"length\":\"长度\"}",
            conflictPairsJson: "[[\"loading\",\"unloading\"]]");

        // Act
        await InvokeSaveAsync(repository, updatedConfig);
        await _context.SaveChangesAsync();

        // Assert
        var savedConfig = await InvokeGetAsync(repository);
        savedConfig.Should().NotBeNull();
        ReadStringProperty(savedConfig!, "EntityAliasesJson").Should().Be("{\"foxconn\":\"富士康\"}");
        ReadStringProperty(savedConfig, "UnitAliasesJson").Should().Be("{\"伏\":\"v\"}");
        ReadStringProperty(savedConfig, "UnitFactorsJson").Should().Be("{\"v\":1}");
        ReadStringProperty(savedConfig, "FieldAliasesJson").Should().Be("{\"length\":\"长度\"}");
        ReadStringProperty(savedConfig, "ConflictPairsJson").Should().Be("[[\"loading\",\"unloading\"]]");

        CountConfigs().Should().Be(1);
    }

    [Fact]
    public async Task GetConfigAsync_ShouldReturnNull_WhenConfigDoesNotExist()
    {
        // Arrange
        var repository = CreateRepository();

        // Act
        var config = await InvokeGetAsync(repository);

        // Assert
        config.Should().BeNull();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private object CreateRepository()
    {
        var repositoryType = Type.GetType(
            "AcceptanceSpecSystem.Data.Repositories.MatchingKnowledgeConfigRepository, AcceptanceSpecSystem.Data");

        repositoryType.Should().NotBeNull("必须实现匹配知识配置仓储");
        return Activator.CreateInstance(repositoryType!, _context)!;
    }

    private object CreateConfig(
        string entityAliasesJson,
        string unitAliasesJson,
        string unitFactorsJson,
        string fieldAliasesJson,
        string conflictPairsJson)
    {
        var entityType = _context.Model.FindEntityType("AcceptanceSpecSystem.Data.Entities.MatchingKnowledgeConfig");
        entityType.Should().NotBeNull("必须注册匹配知识配置实体");

        var clrType = entityType!.ClrType;
        var config = Activator.CreateInstance(clrType)!;

        SetProperty(config, "EntityAliasesJson", entityAliasesJson);
        SetProperty(config, "UnitAliasesJson", unitAliasesJson);
        SetProperty(config, "UnitFactorsJson", unitFactorsJson);
        SetProperty(config, "FieldAliasesJson", fieldAliasesJson);
        SetProperty(config, "ConflictPairsJson", conflictPairsJson);

        return config;
    }

    private async Task<object?> InvokeGetAsync(object repository)
    {
        var method = repository.GetType().GetMethod("GetConfigAsync");
        method.Should().NotBeNull("仓储必须提供 GetConfigAsync 方法");

        var task = method!.Invoke(repository, []) as Task;
        task.Should().NotBeNull();
        await task!;

        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private async Task InvokeSaveAsync(object repository, object config)
    {
        var method = repository.GetType().GetMethod("SaveConfigAsync");
        method.Should().NotBeNull("仓储必须提供 SaveConfigAsync 方法");

        var task = method!.Invoke(repository, [config]) as Task;
        task.Should().NotBeNull();
        await task!;
    }

    private int CountConfigs()
    {
        var entityType = _context.Model.FindEntityType("AcceptanceSpecSystem.Data.Entities.MatchingKnowledgeConfig");
        entityType.Should().NotBeNull();

        var setMethod = typeof(DbContext)
            .GetMethods()
            .Single(method => method.Name == nameof(DbContext.Set) && method.IsGenericMethod && method.GetParameters().Length == 0)
            .MakeGenericMethod(entityType!.ClrType);

        var queryable = setMethod.Invoke(_context, null) as System.Collections.IEnumerable;
        queryable.Should().NotBeNull();

        return queryable!.Cast<object>().Count();
    }

    private static string ReadStringProperty(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName);
        property.Should().NotBeNull($"实体必须包含 {propertyName} 属性");
        return property!.GetValue(target)?.ToString() ?? string.Empty;
    }

    private static void SetProperty(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName);
        property.Should().NotBeNull($"实体必须包含 {propertyName} 属性");
        property!.SetValue(target, value);
    }
}
