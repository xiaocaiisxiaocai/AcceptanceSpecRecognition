using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260719190000_AddColumnMappingRuleNormalizedUniqueKey")]
public sealed class AddColumnMappingRuleNormalizedUniqueKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ScopeKey",
            table: "ColumnMappingRules",
            type: "varchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "NormalizedPattern",
            table: "ColumnMappingRules",
            type: "varchar(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "");

        if (migrationBuilder.ActiveProvider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql(
                """
                UPDATE `ColumnMappingRules`
                SET `ScopeKey` = CASE
                        WHEN `CustomerId` IS NULL THEN 'global'
                        ELSE CONCAT('customer:', `CustomerId`)
                    END,
                    `NormalizedPattern` = UPPER(TRIM(`Pattern`));
                """);

            migrationBuilder.Sql(
                """
                DELETE loser
                FROM `ColumnMappingRules` AS loser
                INNER JOIN `ColumnMappingRules` AS winner
                    ON winner.`ScopeKey` = loser.`ScopeKey`
                    AND winner.`TargetField` = loser.`TargetField`
                    AND winner.`NormalizedPattern` = loser.`NormalizedPattern`
                    AND (
                        winner.`Enabled` > loser.`Enabled`
                        OR (
                            winner.`Enabled` = loser.`Enabled`
                            AND CASE winner.`Source` WHEN 2 THEN 3 WHEN 3 THEN 2 WHEN 1 THEN 1 ELSE 0 END
                                > CASE loser.`Source` WHEN 2 THEN 3 WHEN 3 THEN 2 WHEN 1 THEN 1 ELSE 0 END
                        )
                        OR (
                            winner.`Enabled` = loser.`Enabled`
                            AND CASE winner.`Source` WHEN 2 THEN 3 WHEN 3 THEN 2 WHEN 1 THEN 1 ELSE 0 END
                                = CASE loser.`Source` WHEN 2 THEN 3 WHEN 3 THEN 2 WHEN 1 THEN 1 ELSE 0 END
                            AND winner.`Priority` > loser.`Priority`
                        )
                        OR (
                            winner.`Enabled` = loser.`Enabled`
                            AND CASE winner.`Source` WHEN 2 THEN 3 WHEN 3 THEN 2 WHEN 1 THEN 1 ELSE 0 END
                                = CASE loser.`Source` WHEN 2 THEN 3 WHEN 3 THEN 2 WHEN 1 THEN 1 ELSE 0 END
                            AND winner.`Priority` = loser.`Priority`
                            AND winner.`Id` < loser.`Id`
                        )
                    );
                """);
        }
        else
        {
            migrationBuilder.Sql(
                """
                UPDATE "ColumnMappingRules"
                SET "ScopeKey" = CASE
                        WHEN "CustomerId" IS NULL THEN 'global'
                        ELSE 'customer:' || CAST("CustomerId" AS TEXT)
                    END,
                    "NormalizedPattern" = UPPER(TRIM("Pattern"));
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM "ColumnMappingRules" AS loser
                WHERE EXISTS (
                    SELECT 1
                    FROM "ColumnMappingRules" AS winner
                    WHERE winner."ScopeKey" = loser."ScopeKey"
                      AND winner."TargetField" = loser."TargetField"
                      AND winner."NormalizedPattern" = loser."NormalizedPattern"
                      AND (
                          winner."Enabled" > loser."Enabled"
                          OR (
                              winner."Enabled" = loser."Enabled"
                              AND CASE winner."Source" WHEN 2 THEN 3 WHEN 3 THEN 2 WHEN 1 THEN 1 ELSE 0 END
                                  > CASE loser."Source" WHEN 2 THEN 3 WHEN 3 THEN 2 WHEN 1 THEN 1 ELSE 0 END
                          )
                          OR (
                              winner."Enabled" = loser."Enabled"
                              AND CASE winner."Source" WHEN 2 THEN 3 WHEN 3 THEN 2 WHEN 1 THEN 1 ELSE 0 END
                                  = CASE loser."Source" WHEN 2 THEN 3 WHEN 3 THEN 2 WHEN 1 THEN 1 ELSE 0 END
                              AND winner."Priority" > loser."Priority"
                          )
                          OR (
                              winner."Enabled" = loser."Enabled"
                              AND CASE winner."Source" WHEN 2 THEN 3 WHEN 3 THEN 2 WHEN 1 THEN 1 ELSE 0 END
                                  = CASE loser."Source" WHEN 2 THEN 3 WHEN 3 THEN 2 WHEN 1 THEN 1 ELSE 0 END
                              AND winner."Priority" = loser."Priority"
                              AND winner."Id" < loser."Id"
                          )
                      )
                );
                """);
        }

        migrationBuilder.CreateIndex(
            name: "IX_ColumnMappingRules_ScopeKey_TargetField_NormalizedPattern",
            table: "ColumnMappingRules",
            columns: new[] { "ScopeKey", "TargetField", "NormalizedPattern" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ColumnMappingRules_ScopeKey_TargetField_NormalizedPattern",
            table: "ColumnMappingRules");

        migrationBuilder.DropColumn(
            name: "NormalizedPattern",
            table: "ColumnMappingRules");

        migrationBuilder.DropColumn(
            name: "ScopeKey",
            table: "ColumnMappingRules");
    }
}
