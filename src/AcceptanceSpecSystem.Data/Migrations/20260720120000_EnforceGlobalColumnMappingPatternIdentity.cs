using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations;

public partial class EnforceGlobalColumnMappingPatternIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "GlobalNormalizedPatternKey",
            table: "ColumnMappingRules",
            type: "varchar(200)",
            maxLength: 200,
            nullable: true);

        if (migrationBuilder.ActiveProvider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = BuildAsciiUpperSql("TRIM(`Pattern`)");
            migrationBuilder.Sql(
                $"""
                UPDATE `ColumnMappingRules`
                SET `NormalizedPattern` = {normalized},
                    `GlobalNormalizedPatternKey` = CASE
                        WHEN `CustomerId` IS NULL THEN {normalized}
                        ELSE NULL
                    END;
                """);
            migrationBuilder.Sql(
                """
                DELETE loser
                FROM `ColumnMappingRules` AS loser
                INNER JOIN `ColumnMappingRules` AS winner
                    ON loser.`CustomerId` IS NULL
                    AND winner.`CustomerId` IS NULL
                    AND winner.`GlobalNormalizedPatternKey` = loser.`GlobalNormalizedPatternKey`
                    AND winner.`TargetField` <> loser.`TargetField`
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
            var normalized = BuildAsciiUpperSql("TRIM(\"Pattern\")");
            migrationBuilder.Sql(
                $"""
                UPDATE "ColumnMappingRules"
                SET "NormalizedPattern" = {normalized},
                    "GlobalNormalizedPatternKey" = CASE
                        WHEN "CustomerId" IS NULL THEN {normalized}
                        ELSE NULL
                    END;
                """);
            migrationBuilder.Sql(
                """
                DELETE FROM "ColumnMappingRules" AS loser
                WHERE loser."CustomerId" IS NULL
                  AND EXISTS (
                    SELECT 1
                    FROM "ColumnMappingRules" AS winner
                    WHERE winner."CustomerId" IS NULL
                      AND winner."GlobalNormalizedPatternKey" = loser."GlobalNormalizedPatternKey"
                      AND winner."TargetField" <> loser."TargetField"
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
            name: "IX_ColumnMappingRules_GlobalNormalizedPatternKey",
            table: "ColumnMappingRules",
            column: "GlobalNormalizedPatternKey",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ColumnMappingRules_GlobalNormalizedPatternKey",
            table: "ColumnMappingRules");

        migrationBuilder.DropColumn(
            name: "GlobalNormalizedPatternKey",
            table: "ColumnMappingRules");
    }

    private static string BuildAsciiUpperSql(string expression)
    {
        for (var character = 'a'; character <= 'z'; character++)
        {
            expression = $"REPLACE({expression}, '{character}', '{char.ToUpperInvariant(character)}')";
        }

        return expression;
    }
}
