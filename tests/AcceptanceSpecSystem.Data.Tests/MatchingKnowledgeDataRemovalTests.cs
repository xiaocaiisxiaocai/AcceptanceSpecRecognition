using System.Reflection;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Tests;

public class MatchingKnowledgeDataRemovalTests
{
    [Fact]
    public void AppDbContext_ShouldNotExposeMatchingKnowledgeConfigDbSet()
    {
        var propertyNames = typeof(AppDbContext)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToList();

        propertyNames.Should().NotContain("MatchingKnowledgeConfigs");
    }

    [Fact]
    public void CurrentModelSnapshot_ShouldNotContainMatchingKnowledgeConfigTable()
    {
        var repositoryRoot = TestPathHelper.GetRepositoryRoot();
        var snapshotPath = Path.Combine(
            repositoryRoot,
            "src",
            "AcceptanceSpecSystem.Data",
            "Migrations",
            "AppDbContextModelSnapshot.cs");

        File.ReadAllText(snapshotPath).Should().NotContain("MatchingKnowledgeConfigs");
    }

    [MySqlSmokeFact]
    public async Task MigratedMySqlSchema_ShouldNotContainMatchingKnowledgeConfigTable()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();

        await context.Database.MigrateAsync();

        var tableName = await database.ExecuteScalarAsync("SHOW TABLES LIKE 'MatchingKnowledgeConfigs';");

        tableName.Should().BeNull("真实迁移完成后不应再保留 MatchingKnowledgeConfigs 旧表");
    }
}
