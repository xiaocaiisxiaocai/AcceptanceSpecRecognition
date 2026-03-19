using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAcceptanceSpecOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "AcceptanceSpecs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerOrgUnitId",
                table: "AcceptanceSpecs",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE `AcceptanceSpecs` s
                SET s.`CreatedByUserId` = (
                    SELECT u.`Id`
                    FROM `SystemUsers` u
                    ORDER BY CASE WHEN u.`Username` = 'admin' THEN 0 ELSE 1 END, u.`Id`
                    LIMIT 1
                )
                WHERE s.`CreatedByUserId` IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE `AcceptanceSpecs` s
                SET s.`OwnerOrgUnitId` = COALESCE(
                    (
                        SELECT ou.`Id`
                        FROM `OrgUnits` ou
                        INNER JOIN `SystemUsers` su ON su.`CompanyId` = ou.`CompanyId`
                        WHERE su.`Id` = s.`CreatedByUserId`
                          AND ou.`UnitType` = 0
                          AND ou.`ParentId` IS NULL
                        ORDER BY ou.`Id`
                        LIMIT 1
                    ),
                    (
                        SELECT uo.`OrgUnitId`
                        FROM `AuthUserOrgUnits` uo
                        WHERE uo.`UserId` = s.`CreatedByUserId`
                        ORDER BY uo.`IsPrimary` DESC, uo.`Id`
                        LIMIT 1
                    ),
                    (
                        SELECT ou2.`Id`
                        FROM `OrgUnits` ou2
                        WHERE ou2.`UnitType` = 0
                          AND ou2.`ParentId` IS NULL
                        ORDER BY ou2.`Id`
                        LIMIT 1
                    )
                )
                WHERE s.`OwnerOrgUnitId` IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecs_CreatedByUserId",
                table: "AcceptanceSpecs",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecs_OwnerOrgUnitId",
                table: "AcceptanceSpecs",
                column: "OwnerOrgUnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_AcceptanceSpecs_OrgUnits_OwnerOrgUnitId",
                table: "AcceptanceSpecs",
                column: "OwnerOrgUnitId",
                principalTable: "OrgUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceptanceSpecs_SystemUsers_CreatedByUserId",
                table: "AcceptanceSpecs",
                column: "CreatedByUserId",
                principalTable: "SystemUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AcceptanceSpecs_OrgUnits_OwnerOrgUnitId",
                table: "AcceptanceSpecs");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceptanceSpecs_SystemUsers_CreatedByUserId",
                table: "AcceptanceSpecs");

            migrationBuilder.DropIndex(
                name: "IX_AcceptanceSpecs_CreatedByUserId",
                table: "AcceptanceSpecs");

            migrationBuilder.DropIndex(
                name: "IX_AcceptanceSpecs_OwnerOrgUnitId",
                table: "AcceptanceSpecs");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "AcceptanceSpecs");

            migrationBuilder.DropColumn(
                name: "OwnerOrgUnitId",
                table: "AcceptanceSpecs");
        }
    }
}
