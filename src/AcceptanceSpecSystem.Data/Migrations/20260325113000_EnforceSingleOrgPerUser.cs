using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260325113000_EnforceSingleOrgPerUser")]
    public partial class EnforceSingleOrgPerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE current_row
                FROM AuthUserOrgUnits current_row
                INNER JOIN (
                    SELECT UserId, SUM(CASE WHEN IsPrimary = 1 THEN 1 ELSE 0 END) AS PrimaryCount
                    FROM AuthUserOrgUnits
                    GROUP BY UserId
                ) primary_stats ON primary_stats.UserId = current_row.UserId
                INNER JOIN AuthUserOrgUnits keep_row
                    ON keep_row.UserId = current_row.UserId
                   AND keep_row.Id <> current_row.Id
                WHERE
                    CASE
                        WHEN primary_stats.PrimaryCount = 1 AND keep_row.IsPrimary = 1 THEN 0
                        ELSE 1
                    END
                    <
                    CASE
                        WHEN primary_stats.PrimaryCount = 1 AND current_row.IsPrimary = 1 THEN 0
                        ELSE 1
                    END
                    OR (
                        CASE
                            WHEN primary_stats.PrimaryCount = 1 AND keep_row.IsPrimary = 1 THEN 0
                            ELSE 1
                        END
                        =
                        CASE
                            WHEN primary_stats.PrimaryCount = 1 AND current_row.IsPrimary = 1 THEN 0
                            ELSE 1
                        END
                        AND (
                            keep_row.CreatedAt < current_row.CreatedAt
                            OR (
                                keep_row.CreatedAt = current_row.CreatedAt
                                AND keep_row.Id < current_row.Id
                            )
                        )
                    );
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
