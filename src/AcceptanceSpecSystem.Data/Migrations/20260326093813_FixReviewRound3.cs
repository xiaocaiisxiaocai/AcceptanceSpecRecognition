using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixReviewRound3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "MatchingFillTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "MatchingFillTasks",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchingFillTasks_CompanyId_CreatedByUserId_CreatedAt",
                table: "MatchingFillTasks",
                columns: new[] { "CompanyId", "CreatedByUserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MatchingFillTasks_CompanyId_CreatedByUserId_CreatedAt",
                table: "MatchingFillTasks");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "MatchingFillTasks");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "MatchingFillTasks");
        }
    }
}
