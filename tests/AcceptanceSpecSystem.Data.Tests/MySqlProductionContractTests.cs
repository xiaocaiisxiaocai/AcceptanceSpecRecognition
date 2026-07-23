using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using AcceptanceSpecSystem.Data.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Tests;

/// <summary>
/// 只在显式启用的真实 MySQL 8 环境运行，验证 SQLite/InMemory 无法覆盖的 provider 契约。
/// </summary>
public sealed class MySqlProductionContractTests
{
    [MySqlSmokeFact]
    public async Task MySql8_ShouldHonorUtf8TimeZoneUniqueConstraintAndRepositoryOrdering()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        await context.Database.MigrateAsync();

        (await database.ExecuteScalarAsync("SELECT @@character_set_database;"))
            .Should().Be("utf8mb4");
        (await database.ExecuteScalarAsync("SELECT @@collation_database;"))
            .Should().Be("utf8mb4_unicode_ci");
        Convert.ToInt32(await database.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM information_schema.COLUMNS " +
                "WHERE TABLE_SCHEMA = DATABASE() AND COLLATION_NAME IS NOT NULL " +
                "AND COLLATION_NAME <> 'utf8mb4_unicode_ci';"))
            .Should().Be(0, "所有文本列都必须继承稳定的 utf8mb4_unicode_ci 排序规则");
        Convert.ToInt32(await database.ExecuteScalarAsync(
                "SELECT ABS(TIMESTAMPDIFF(SECOND, UTC_TIMESTAMP(), NOW()));"))
            .Should().BeLessThanOrEqualTo(1, "CI MySQL 服务固定使用 UTC");

        context.Customers.Add(new Customer { Name = "示例客户😀" });
        context.ColumnMappingRules.AddRange(
            new ColumnMappingRule
            {
                TargetField = ColumnMappingTargetField.Specification,
                Pattern = "规格-low",
                Priority = 10
            },
            new ColumnMappingRule
            {
                TargetField = ColumnMappingTargetField.Project,
                Pattern = "项目",
                Priority = 1
            },
            new ColumnMappingRule
            {
                TargetField = ColumnMappingTargetField.Specification,
                Pattern = "规格-high",
                Priority = 20
            });
        await context.SaveChangesAsync();

        var orderedRules = await new ColumnMappingRuleRepository(context).GetEnabledOrderedAsync();
        orderedRules.Select(rule => rule.Pattern)
            .Should().Equal("项目", "规格-high", "规格-low");

        context.Customers.Add(new Customer { Name = "CaseSensitiveContract" });
        await context.SaveChangesAsync();
        context.Customers.Add(new Customer { Name = "casesensitivecontract" });

        var saveDuplicate = async () => await context.SaveChangesAsync();
        await saveDuplicate.Should().ThrowAsync<DbUpdateException>(
            "utf8mb4_unicode_ci 下客户名称唯一约束应大小写不敏感");
    }
}
