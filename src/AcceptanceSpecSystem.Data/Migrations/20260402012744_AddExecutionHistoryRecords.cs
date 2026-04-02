using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionHistoryRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExecutionHistoryRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TaskId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TaskType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceFileId = table.Column<int>(type: "int", nullable: true),
                    SourceFileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceFileType = table.Column<int>(type: "int", nullable: true),
                    FileCount = table.Column<int>(type: "int", nullable: false),
                    TotalRowCount = table.Column<int>(type: "int", nullable: false),
                    MatchedRowCount = table.Column<int>(type: "int", nullable: false),
                    AdoptedRowCount = table.Column<int>(type: "int", nullable: false),
                    UnmatchedRowCount = table.Column<int>(type: "int", nullable: false),
                    SkippedRowCount = table.Column<int>(type: "int", nullable: false),
                    NotAdoptedRowCount = table.Column<int>(type: "int", nullable: false),
                    ManualSelectedRowCount = table.Column<int>(type: "int", nullable: false),
                    DetailJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionHistoryRecords", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionHistoryRecords_CompanyId_CreatedByUserId_CreatedAt",
                table: "ExecutionHistoryRecords",
                columns: new[] { "CompanyId", "CreatedByUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionHistoryRecords_CreatedAt",
                table: "ExecutionHistoryRecords",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionHistoryRecords_TaskId",
                table: "ExecutionHistoryRecords",
                column: "TaskId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionHistoryRecords");
        }
    }
}
