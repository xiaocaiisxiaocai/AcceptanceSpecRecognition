using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class RestrictAcceptanceSpecReferenceDeletes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AcceptanceSpecs_Customers_CustomerId",
                table: "AcceptanceSpecs");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceptanceSpecs_MachineModels_MachineModelId",
                table: "AcceptanceSpecs");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceptanceSpecs_Processes_ProcessId",
                table: "AcceptanceSpecs");

            migrationBuilder.AddForeignKey(
                name: "FK_AcceptanceSpecs_Customers_CustomerId",
                table: "AcceptanceSpecs",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceptanceSpecs_MachineModels_MachineModelId",
                table: "AcceptanceSpecs",
                column: "MachineModelId",
                principalTable: "MachineModels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceptanceSpecs_Processes_ProcessId",
                table: "AcceptanceSpecs",
                column: "ProcessId",
                principalTable: "Processes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AcceptanceSpecs_Customers_CustomerId",
                table: "AcceptanceSpecs");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceptanceSpecs_MachineModels_MachineModelId",
                table: "AcceptanceSpecs");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceptanceSpecs_Processes_ProcessId",
                table: "AcceptanceSpecs");

            migrationBuilder.AddForeignKey(
                name: "FK_AcceptanceSpecs_Customers_CustomerId",
                table: "AcceptanceSpecs",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceptanceSpecs_MachineModels_MachineModelId",
                table: "AcceptanceSpecs",
                column: "MachineModelId",
                principalTable: "MachineModels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceptanceSpecs_Processes_ProcessId",
                table: "AcceptanceSpecs",
                column: "ProcessId",
                principalTable: "Processes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
