using System.Reflection;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Migrations;
using AcceptanceSpecSystem.Data.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AcceptanceSpecSystem.Data.Tests;

public sealed class ColumnMappingRuleUniqueKeyMigrationTests
{
    private const string PreviousMigrationId = "20260719170000_BackfillDocumentTemplateRegions";
    private const string MigrationId = "20260719190000_AddColumnMappingRuleNormalizedUniqueKey";
    private const string GlobalIdentityMigrationId = "20260720120000_EnforceGlobalColumnMappingPatternIdentity";

    [Theory]
    [InlineData("Pomelo.EntityFrameworkCore.MySql", "CONCAT('customer:'", "DELETE loser")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", "'customer:' || CAST", "WHERE EXISTS")]
    public void Migration_ShouldBackfillScopeAndPatternBeforeDeduplicatingAndCreatingUniqueIndex(
        string provider,
        string expectedScopeSql,
        string expectedDeleteSql)
    {
        var migration = new AddColumnMappingRuleNormalizedUniqueKey();
        var builder = new MigrationBuilder(provider);
        typeof(AddColumnMappingRuleNormalizedUniqueKey)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, new object[] { builder });

        var operations = builder.Operations;
        var sql = operations.OfType<SqlOperation>().Select(operation => operation.Sql).ToArray();
        sql.Should().HaveCount(2);
        sql[0].Should().Contain(expectedScopeSql).And.Contain("UPPER(TRIM");
        sql[1].Should().Contain(expectedDeleteSql)
            .And.Contain("winner")
            .And.Contain("loser")
            .And.Contain("WHEN 2 THEN 3 WHEN 3 THEN 2");
        operations.Last().Should().BeOfType<CreateIndexOperation>()
            .Which.IsUnique.Should().BeTrue();
    }

    [Theory]
    [InlineData("Pomelo.EntityFrameworkCore.MySql")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite")]
    public void GlobalIdentityMigration_ShouldUseRuntimeCompatibleAsciiNormalizationAndUniqueIndex(
        string provider)
    {
        var migration = new EnforceGlobalColumnMappingPatternIdentity();
        var builder = new MigrationBuilder(provider);
        typeof(EnforceGlobalColumnMappingPatternIdentity)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, new object[] { builder });

        var sql = builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql).ToArray();
        sql.Should().HaveCount(2);
        sql[0].Should().Contain("GlobalNormalizedPatternKey")
            .And.Contain("REPLACE(")
            .And.NotContain("UPPER(");
        sql[1].Should().Contain("winner").And.Contain("loser");
        var index = builder.Operations.OfType<CreateIndexOperation>().Single();
        index.Name.Should().Be("IX_ColumnMappingRules_GlobalNormalizedPatternKey");
        index.IsUnique.Should().BeTrue();
    }

    [Fact]
    public void NormalizePattern_ShouldUseInvariantUnicodeCaseNormalization()
    {
        ColumnMappingRule.NormalizePattern("  ascii-α  ").Should().Be("ASCII-Α");
        ColumnMappingRule.NormalizePattern("Α").Should().Be("Α");
        ColumnMappingRule.NormalizePattern("α").Should().Be("Α");
    }

    [MySqlSmokeFact]
    public async Task Migration_ShouldDeduplicateLegacyGlobalRowsAndEnforceNormalizedIdentity()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigrationId);

        // 迁移前模型尚无规范键列，使用 SQL 写入历史结构，避免当前模型字段参与 INSERT。
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO ColumnMappingRules " +
            "(TargetField, MatchMode, Pattern, Priority, Enabled, Source, CustomerId, CreatedAt) VALUES " +
            "(1, 1, '  Legacy Header ', 100, 1, 1, NULL, UTC_TIMESTAMP()), " +
            "(1, 2, 'legacy header', 1, 1, 2, NULL, UTC_TIMESTAMP());");

        await migrator.MigrateAsync(MigrationId);

        Convert.ToInt32(await database.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM ColumnMappingRules " +
                "WHERE ScopeKey = 'global' AND TargetField = 1 AND NormalizedPattern = 'LEGACY HEADER';"))
            .Should().Be(1);
        Convert.ToInt32(await database.ExecuteScalarAsync(
                "SELECT Source FROM ColumnMappingRules " +
                "WHERE ScopeKey = 'global' AND TargetField = 1 AND NormalizedPattern = 'LEGACY HEADER';"))
            .Should().Be((int)ColumnMappingRuleSource.Manual);
    }

    [MySqlSmokeFact]
    public async Task GlobalIdentityMigration_ShouldDeduplicateConflictingTargets()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationId);

        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO ColumnMappingRules " +
            "(TargetField, MatchMode, Pattern, ScopeKey, NormalizedPattern, Priority, Enabled, Source, CustomerId, CreatedAt) VALUES " +
            "(1, 2, 'shared header', 'global', 'SHARED HEADER', 10, 1, 2, NULL, UTC_TIMESTAMP()), " +
            "(2, 2, 'shared header', 'global', 'SHARED HEADER', 100, 1, 3, NULL, UTC_TIMESTAMP());");

        await migrator.MigrateAsync(GlobalIdentityMigrationId);

        Convert.ToInt32(await database.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM ColumnMappingRules " +
                "WHERE GlobalNormalizedPatternKey = 'SHARED HEADER';"))
            .Should().Be(1);
        Convert.ToInt32(await database.ExecuteScalarAsync(
                "SELECT TargetField FROM ColumnMappingRules " +
                "WHERE GlobalNormalizedPatternKey = 'SHARED HEADER';"))
            .Should().Be((int)ColumnMappingTargetField.Project,
                "手工全局规则应优先于学习规则保留");
    }
}
