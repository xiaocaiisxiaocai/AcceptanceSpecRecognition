using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentImportExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentImportExecutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RequestKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestFingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceFileId = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    CleanupRequested = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CleanupCompleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ResultJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Message = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentImportExecutions", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentImportExecutions_CompanyId_CreatedByUserId_CreatedAt",
                table: "DocumentImportExecutions",
                columns: new[] { "CompanyId", "CreatedByUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentImportExecutions_CreatedAt",
                table: "DocumentImportExecutions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentImportExecutions_RequestKey",
                table: "DocumentImportExecutions",
                column: "RequestKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentImportExecutions");
        }
    }
}
