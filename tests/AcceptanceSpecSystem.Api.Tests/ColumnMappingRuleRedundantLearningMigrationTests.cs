using System.Reflection;
using AcceptanceSpecSystem.Data;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class ColumnMappingRuleRedundantLearningMigrationTests
{
    private const string MigrationTypeName =
        "AcceptanceSpecSystem.Data.Migrations.RemoveRedundantCustomerLearnedColumnRules";
    private const string MigrationId =
        "20260730120000_RemoveRedundantCustomerLearnedColumnRules";

    [Theory]
    [InlineData("Pomelo.EntityFrameworkCore.MySql", "DELETE customerRule", "`MatchMode` IN (1, 2)")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", "DELETE FROM \"ColumnMappingRules\"", "\"MatchMode\" IN (1, 2)")]
    public void Migration_ShouldProvideProviderCompatibleScopedCleanupSql(
        string provider,
        string expectedDelete,
        string expectedMatchModeFilter)
    {
        var migrationType = typeof(AppDbContext).Assembly.GetType(MigrationTypeName);
        migrationType.Should().NotBeNull();
        var migration = (Migration)Activator.CreateInstance(migrationType!)!;
        var builder = new MigrationBuilder(provider);
        migrationType!
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var sql = builder.Operations.OfType<SqlOperation>().Should().ContainSingle().Subject.Sql;
        sql.Should().Contain(expectedDelete)
            .And.Contain(expectedMatchModeFilter)
            .And.Contain("CustomerId")
            .And.Contain("Source")
            .And.Contain("Enabled")
            .And.Contain("TargetField")
            .And.Contain("NormalizedPattern");
        DatabaseInitializer.ClassifyMigration(MigrationId)
            .Should().Be(DatabaseMigrationRisk.Destructive);
    }

    [Fact]
    public async Task Migration_ShouldRemoveOnlyEnabledLearnedRulesCoveredBySafeGlobalRules()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var safePattern = ColumnMappingRule.NormalizePattern("安全全局词");
        var regexPattern = ColumnMappingRule.NormalizePattern("正则全局词");
        var disabledPattern = ColumnMappingRule.NormalizePattern("禁用全局词");
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "ColumnMappingRules"
                ("TargetField", "MatchMode", "Pattern", "ScopeKey", "NormalizedPattern",
                 "GlobalNormalizedPatternKey", "Priority", "Enabled", "Source", "CustomerId", "CreatedAt")
            VALUES
                (3, 2, '安全全局词', 'global', {0}, {0}, 80, 1, 3, NULL, CURRENT_TIMESTAMP),
                (3, 2, '安全全局词', 'customer:101', {0}, NULL, 100, 1, 3, 101, CURRENT_TIMESTAMP),
                (3, 2, '安全全局词', 'customer:102', {0}, NULL, 100, 1, 2, 102, CURRENT_TIMESTAMP),
                (3, 2, '安全全局词', 'customer:103', {0}, NULL, 100, 0, 3, 103, CURRENT_TIMESTAMP),
                (4, 2, '安全全局词', 'customer:104', {0}, NULL, 100, 1, 3, 104, CURRENT_TIMESTAMP),
                (3, 3, '安全全局词', 'customer:107', {0}, NULL, 100, 1, 3, 107, CURRENT_TIMESTAMP),
                (3, 3, '正则全局词', 'global', {1}, {1}, 80, 1, 3, NULL, CURRENT_TIMESTAMP),
                (3, 2, '正则全局词', 'customer:105', {1}, NULL, 100, 1, 3, 105, CURRENT_TIMESTAMP),
                (3, 2, '禁用全局词', 'global', {2}, {2}, 80, 0, 3, NULL, CURRENT_TIMESTAMP),
                (3, 2, '禁用全局词', 'customer:106', {2}, NULL, 100, 1, 3, 106, CURRENT_TIMESTAMP);
            """,
            safePattern,
            regexPattern,
            disabledPattern);

        var migrationType = typeof(AppDbContext).Assembly.GetType(MigrationTypeName);
        migrationType.Should().NotBeNull();
        var migration = (Migration)Activator.CreateInstance(migrationType!)!;
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");
        migrationType!
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        foreach (var operation in builder.Operations.OfType<SqlOperation>())
        {
            await context.Database.ExecuteSqlRawAsync(operation.Sql);
        }

        var remaining = await context.ColumnMappingRules.AsNoTracking().ToListAsync();
        remaining.Should().NotContain(rule =>
            rule.CustomerId == 101 &&
            rule.TargetField == ColumnMappingTargetField.Acceptance);
        remaining.Should().Contain(rule => rule.CustomerId == 102 && rule.Source == ColumnMappingRuleSource.Manual);
        remaining.Should().Contain(rule => rule.CustomerId == 103 && !rule.Enabled);
        remaining.Should().Contain(rule => rule.CustomerId == 104 && rule.TargetField == ColumnMappingTargetField.Remark);
        remaining.Should().Contain(rule => rule.CustomerId == 105);
        remaining.Should().Contain(rule => rule.CustomerId == 106);
        remaining.Should().Contain(rule => rule.CustomerId == 107 && rule.MatchMode == ColumnMappingMatchMode.Regex);
    }
}
