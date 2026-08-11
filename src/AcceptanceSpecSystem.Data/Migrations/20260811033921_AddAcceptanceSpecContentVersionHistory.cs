using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAcceptanceSpecContentVersionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcceptanceSpecContentVersions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcceptanceSpecId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Project = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Specification = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Acceptance = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Remark = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ChangedByUserId = table.Column<int>(type: "int", nullable: true),
                    ChangedByNameSnapshot = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChangeSource = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChangeReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RestoredFromVersion = table.Column<long>(type: "bigint", nullable: true),
                    IsMigrationBaseline = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceptanceSpecContentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcceptanceSpecContentVersions_AcceptanceSpecs_AcceptanceSpec~",
                        column: x => x.AcceptanceSpecId,
                        principalTable: "AcceptanceSpecs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AcceptanceSpecContentVersions_SystemUsers_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "SystemUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecContentVersions_AcceptanceSpecId_Version",
                table: "AcceptanceSpecContentVersions",
                columns: new[] { "AcceptanceSpecId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecContentVersions_AcceptanceSpecId_Version_Id",
                table: "AcceptanceSpecContentVersions",
                columns: new[] { "AcceptanceSpecId", "Version", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecContentVersions_ChangedByUserId",
                table: "AcceptanceSpecContentVersions",
                column: "ChangedByUserId");

            migrationBuilder.Sql(
                """
                INSERT INTO `AcceptanceSpecContentVersions`
                    (`AcceptanceSpecId`, `Version`, `Project`, `Specification`, `Acceptance`, `Remark`,
                     `ChangedAtUtc`, `ChangedByUserId`, `ChangedByNameSnapshot`, `ChangeSource`,
                     `ChangeReason`, `RestoredFromVersion`, `IsMigrationBaseline`)
                SELECT
                    `Id`, `ReferenceVersion`, `Project`, `Specification`, `Acceptance`, `Remark`,
                    COALESCE(`UpdatedAt`, `ImportedAt`), NULL, NULL, 'migration-baseline',
                    NULL, NULL, 1
                FROM `AcceptanceSpecs`;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcceptanceSpecContentVersions");
        }
    }
}
