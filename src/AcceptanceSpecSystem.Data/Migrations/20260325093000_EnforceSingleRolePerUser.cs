using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260325093000_EnforceSingleRolePerUser")]
    public partial class EnforceSingleRolePerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE current_row
                FROM AuthUserRoles current_row
                INNER JOIN AuthRoles current_role ON current_role.Id = current_row.RoleId
                INNER JOIN AuthUserRoles keep_row
                    ON keep_row.UserId = current_row.UserId
                   AND keep_row.Id <> current_row.Id
                INNER JOIN AuthRoles keep_role ON keep_role.Id = keep_row.RoleId
                WHERE
                    CASE WHEN LOWER(keep_role.Code) = 'admin' THEN 0 ELSE 1 END
                    <
                    CASE WHEN LOWER(current_role.Code) = 'admin' THEN 0 ELSE 1 END
                    OR (
                        CASE WHEN LOWER(keep_role.Code) = 'admin' THEN 0 ELSE 1 END
                        =
                        CASE WHEN LOWER(current_role.Code) = 'admin' THEN 0 ELSE 1 END
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
