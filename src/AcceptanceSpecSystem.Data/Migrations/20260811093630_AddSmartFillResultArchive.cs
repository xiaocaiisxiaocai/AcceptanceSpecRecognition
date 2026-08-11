using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartFillResultArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResultArchiveContentType",
                table: "ExecutionHistoryRecords",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ResultArchiveFileName",
                table: "ExecutionHistoryRecords",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ResultArchiveRelativePath",
                table: "ExecutionHistoryRecords",
                type: "varchar(512)",
                maxLength: 512,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ResultArchiveSha256",
                table: "ExecutionHistoryRecords",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "ResultArchiveSizeBytes",
                table: "ExecutionHistoryRecords",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResultArchiveContentType",
                table: "ExecutionHistoryRecords");

            migrationBuilder.DropColumn(
                name: "ResultArchiveFileName",
                table: "ExecutionHistoryRecords");

            migrationBuilder.DropColumn(
                name: "ResultArchiveRelativePath",
                table: "ExecutionHistoryRecords");

            migrationBuilder.DropColumn(
                name: "ResultArchiveSha256",
                table: "ExecutionHistoryRecords");

            migrationBuilder.DropColumn(
                name: "ResultArchiveSizeBytes",
                table: "ExecutionHistoryRecords");
        }
    }
}
