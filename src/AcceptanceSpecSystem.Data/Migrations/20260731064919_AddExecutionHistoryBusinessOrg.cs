using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionHistoryBusinessOrg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OwnerOrgUnitId",
                table: "ExecutionHistoryRecords",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionHistoryRecords_CompanyId_OwnerOrgUnitId_CreatedAt",
                table: "ExecutionHistoryRecords",
                columns: new[] { "CompanyId", "OwnerOrgUnitId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExecutionHistoryRecords_CompanyId_OwnerOrgUnitId_CreatedAt",
                table: "ExecutionHistoryRecords");

            migrationBuilder.DropColumn(
                name: "OwnerOrgUnitId",
                table: "ExecutionHistoryRecords");
        }
    }
}
