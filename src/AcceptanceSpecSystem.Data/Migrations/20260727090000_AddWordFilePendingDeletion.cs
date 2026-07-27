using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWordFilePendingDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MatchingFillTasks_WordFiles_SourceFileId",
                table: "MatchingFillTasks");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletionLeaseExpiresAt",
                table: "WordFiles",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionLeaseToken",
                table: "WordFiles",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletionRequestedAt",
                table: "WordFiles",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeletionRetryCount",
                table: "WordFiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DeletionStatus",
                table: "WordFiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastDeletionError",
                table: "WordFiles",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "NextDeletionAttemptAt",
                table: "WordFiles",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WordFiles_DeletionStatus_NextDeletionAttemptAt_Id",
                table: "WordFiles",
                columns: new[] { "DeletionStatus", "NextDeletionAttemptAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentImportExecutions_SourceFileId",
                table: "DocumentImportExecutions",
                column: "SourceFileId");

            migrationBuilder.AddForeignKey(
                name: "FK_MatchingFillTasks_WordFiles_SourceFileId",
                table: "MatchingFillTasks",
                column: "SourceFileId",
                principalTable: "WordFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MatchingFillTasks_WordFiles_SourceFileId",
                table: "MatchingFillTasks");

            migrationBuilder.DropIndex(
                name: "IX_WordFiles_DeletionStatus_NextDeletionAttemptAt_Id",
                table: "WordFiles");

            migrationBuilder.DropIndex(
                name: "IX_DocumentImportExecutions_SourceFileId",
                table: "DocumentImportExecutions");

            migrationBuilder.DropColumn(
                name: "DeletionLeaseExpiresAt",
                table: "WordFiles");

            migrationBuilder.DropColumn(
                name: "DeletionLeaseToken",
                table: "WordFiles");

            migrationBuilder.DropColumn(
                name: "DeletionRequestedAt",
                table: "WordFiles");

            migrationBuilder.DropColumn(
                name: "DeletionRetryCount",
                table: "WordFiles");

            migrationBuilder.DropColumn(
                name: "DeletionStatus",
                table: "WordFiles");

            migrationBuilder.DropColumn(
                name: "LastDeletionError",
                table: "WordFiles");

            migrationBuilder.DropColumn(
                name: "NextDeletionAttemptAt",
                table: "WordFiles");

            migrationBuilder.AddForeignKey(
                name: "FK_MatchingFillTasks_WordFiles_SourceFileId",
                table: "MatchingFillTasks",
                column: "SourceFileId",
                principalTable: "WordFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
