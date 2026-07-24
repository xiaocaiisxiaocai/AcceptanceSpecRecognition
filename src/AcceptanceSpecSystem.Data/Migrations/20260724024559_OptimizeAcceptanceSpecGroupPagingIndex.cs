using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeAcceptanceSpecGroupPagingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcceptanceSpecs_CustomerId_ProcessId_MachineModelId",
                table: "AcceptanceSpecs");

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecs_CustomerId_ProcessId_MachineModelId_Imported~",
                table: "AcceptanceSpecs",
                columns: new[] { "CustomerId", "ProcessId", "MachineModelId", "ImportedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcceptanceSpecs_CustomerId_ProcessId_MachineModelId_Imported~",
                table: "AcceptanceSpecs");

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecs_CustomerId_ProcessId_MachineModelId",
                table: "AcceptanceSpecs",
                columns: new[] { "CustomerId", "ProcessId", "MachineModelId" });
        }
    }
}
