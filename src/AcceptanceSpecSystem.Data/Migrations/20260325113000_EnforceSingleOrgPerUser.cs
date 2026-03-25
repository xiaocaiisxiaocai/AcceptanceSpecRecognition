using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleOrgPerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE auo
                FROM AuthUserOrgUnits auo
                INNER JOIN (
                    SELECT ranked.Id
                    FROM (
                        SELECT
                            auo.Id,
                            ROW_NUMBER() OVER (
                                PARTITION BY auo.UserId
                                ORDER BY
                                    CASE
                                        WHEN primary_stats.PrimaryCount = 1 AND auo.IsPrimary = 1 THEN 0
                                        ELSE 1
                                    END,
                                    auo.CreatedAt,
                                    auo.Id
                            ) AS rn
                        FROM AuthUserOrgUnits auo
                        INNER JOIN (
                            SELECT UserId, SUM(CASE WHEN IsPrimary = 1 THEN 1 ELSE 0 END) AS PrimaryCount
                            FROM AuthUserOrgUnits
                            GROUP BY UserId
                        ) primary_stats ON primary_stats.UserId = auo.UserId
                    ) ranked
                    WHERE ranked.rn > 1
                ) duplicates ON duplicates.Id = auo.Id;
                """);

            migrationBuilder.Sql("""
                UPDATE AuthUserOrgUnits
                SET IsPrimary = 1
                WHERE UserId IN (
                    SELECT single_org.UserId
                    FROM (
                        SELECT UserId
                        FROM AuthUserOrgUnits
                        GROUP BY UserId
                        HAVING COUNT(*) = 1
                    ) single_org
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO AuthUserOrgUnits (UserId, OrgUnitId, IsPrimary, StartAt, EndAt, CreatedAt)
                SELECT su.Id, rootOrg.Id, 1, NULL, NULL, CURRENT_TIMESTAMP
                FROM SystemUsers su
                INNER JOIN OrgUnits rootOrg
                    ON rootOrg.CompanyId = su.CompanyId
                   AND rootOrg.UnitType = 0
                   AND rootOrg.ParentId IS NULL
                LEFT JOIN AuthUserOrgUnits auo ON auo.UserId = su.Id
                WHERE auo.Id IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AuthUserOrgUnits_UserId",
                table: "AuthUserOrgUnits",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuthUserOrgUnits_UserId",
                table: "AuthUserOrgUnits");
        }
    }
}
