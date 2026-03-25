using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleRolePerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE aur
                FROM AuthUserRoles aur
                INNER JOIN (
                    SELECT ranked.Id
                    FROM (
                        SELECT
                            aur.Id,
                            ROW_NUMBER() OVER (
                                PARTITION BY aur.UserId
                                ORDER BY
                                    CASE WHEN LOWER(ar.Code) = 'admin' THEN 0 ELSE 1 END,
                                    aur.CreatedAt,
                                    aur.Id
                            ) AS rn
                        FROM AuthUserRoles aur
                        INNER JOIN AuthRoles ar ON ar.Id = aur.RoleId
                    ) ranked
                    WHERE ranked.rn > 1
                ) duplicates ON duplicates.Id = aur.Id;
                """);

            migrationBuilder.Sql("""
                INSERT INTO AuthUserRoles (UserId, RoleId, StartAt, EndAt, CreatedAt)
                SELECT su.Id, commonRole.Id, NULL, NULL, CURRENT_TIMESTAMP
                FROM SystemUsers su
                INNER JOIN AuthRoles commonRole
                    ON commonRole.CompanyId = su.CompanyId
                   AND commonRole.Code = 'common'
                LEFT JOIN AuthUserRoles aur ON aur.UserId = su.Id
                WHERE aur.Id IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AuthUserRoles_UserId",
                table: "AuthUserRoles",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuthUserRoles_UserId",
                table: "AuthUserRoles");
        }
    }
}
