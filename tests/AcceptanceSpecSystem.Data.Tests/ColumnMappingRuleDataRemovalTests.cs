using System.Reflection;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Repositories;
using AcceptanceSpecSystem.Data.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Tests;

public class ColumnMappingRuleDataRecoveryTests
{
    [Fact]
    public void AppDbContext_ShouldExposeColumnMappingRulesDbSet()
    {
        var propertyNames = typeof(AppDbContext)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToList();

        propertyNames.Should().Contain("ColumnMappingRules");
    }

    [Fact]
    public void IUnitOfWork_ShouldExposeColumnMappingRulesRepository()
    {
        var propertyNames = typeof(IUnitOfWork)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToList();

        propertyNames.Should().Contain("ColumnMappingRules");
    }

    [Fact]
    public void CurrentModelSnapshot_ShouldContainColumnMappingRulesTable()
    {
        var repositoryRoot = TestPathHelper.GetRepositoryRoot();
        var snapshotPath = Path.Combine(
            repositoryRoot,
            "src",
            "AcceptanceSpecSystem.Data",
            "Migrations",
            "AppDbContextModelSnapshot.cs");

        File.ReadAllText(snapshotPath).Should().Contain("ColumnMappingRules");
    }

    [MySqlSmokeFact]
    public async Task MigratedMySqlSchema_ShouldContainColumnMappingRulesTable()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();

        await context.Database.MigrateAsync();

        var tableName = await database.ExecuteScalarAsync("SHOW TABLES LIKE 'ColumnMappingRules';");

        tableName.Should().NotBeNull("真实迁移完成后应恢复 ColumnMappingRules 表");
    }
}
