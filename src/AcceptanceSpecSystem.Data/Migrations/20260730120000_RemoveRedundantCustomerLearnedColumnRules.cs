using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260730120000_RemoveRedundantCustomerLearnedColumnRules")]
public sealed class RemoveRedundantCustomerLearnedColumnRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql(
                """
                DELETE customerRule
                FROM `ColumnMappingRules` AS customerRule
                INNER JOIN `ColumnMappingRules` AS globalRule
                    ON globalRule.`CustomerId` IS NULL
                    AND globalRule.`Enabled` = 1
                    AND globalRule.`TargetField` = customerRule.`TargetField`
                    AND globalRule.`NormalizedPattern` = customerRule.`NormalizedPattern`
                    AND globalRule.`MatchMode` IN (1, 2)
                WHERE customerRule.`CustomerId` IS NOT NULL
                  AND customerRule.`Source` = 3
                  AND customerRule.`Enabled` = 1
                  AND customerRule.`MatchMode` = 2;
                """);
            return;
        }

        migrationBuilder.Sql(
            """
            DELETE FROM "ColumnMappingRules" AS customerRule
            WHERE customerRule."CustomerId" IS NOT NULL
              AND customerRule."Source" = 3
              AND customerRule."Enabled" = 1
              AND customerRule."MatchMode" = 2
              AND EXISTS (
                  SELECT 1
                  FROM "ColumnMappingRules" AS globalRule
                  WHERE globalRule."CustomerId" IS NULL
                    AND globalRule."Enabled" = 1
                    AND globalRule."TargetField" = customerRule."TargetField"
                    AND globalRule."NormalizedPattern" = customerRule."NormalizedPattern"
                    AND globalRule."MatchMode" IN (1, 2)
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // 已删除的自动学习副本无法在不伪造客户学习历史的情况下安全恢复。
        // 对应启用全局规则仍保留相同映射行为。
    }
}
