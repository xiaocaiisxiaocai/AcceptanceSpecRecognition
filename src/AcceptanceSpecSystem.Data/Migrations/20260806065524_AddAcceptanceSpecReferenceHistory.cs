using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAcceptanceSpecReferenceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ReferenceVersion",
                table: "AcceptanceSpecs",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "AcceptanceSpecReferenceEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcceptanceSpecId = table.Column<int>(type: "int", nullable: false),
                    ReferenceVersion = table.Column<long>(type: "bigint", nullable: false),
                    TaskId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TaskOccurrenceIndex = table.Column<int>(type: "int", nullable: true),
                    OccurrenceCount = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    ReferencedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceptanceSpecReferenceEvents", x => x.Id);
                    table.CheckConstraint("CK_AcceptanceSpecReferenceEvents_OccurrenceCount", "`OccurrenceCount` > 0");
                    table.ForeignKey(
                        name: "FK_AcceptanceSpecReferenceEvents_AcceptanceSpecs_AcceptanceSpec~",
                        column: x => x.AcceptanceSpecId,
                        principalTable: "AcceptanceSpecs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecReferenceEvents_AcceptanceSpecId_ReferenceVers~",
                table: "AcceptanceSpecReferenceEvents",
                columns: new[] { "AcceptanceSpecId", "ReferenceVersion", "ReferencedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecReferenceEvents_TaskId_AcceptanceSpecId_Refere~",
                table: "AcceptanceSpecReferenceEvents",
                columns: new[] { "TaskId", "AcceptanceSpecId", "ReferenceVersion", "TaskOccurrenceIndex" },
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO `AcceptanceSpecReferenceEvents`
                    (`AcceptanceSpecId`, `ReferenceVersion`, `TaskId`, `TaskOccurrenceIndex`, `OccurrenceCount`, `ReferencedAtUtc`)
                SELECT
                    `Id`, 1, NULL, NULL, `ReferenceCount`, NULL
                FROM `AcceptanceSpecs`
                WHERE `ReferenceCount` > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcceptanceSpecReferenceEvents");

            migrationBuilder.DropColumn(
                name: "ReferenceVersion",
                table: "AcceptanceSpecs");
        }
    }
}
