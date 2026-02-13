using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMachineModelAndOptionalProcess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AcceptanceSpecs_Customers_CustomerId",
                table: "AcceptanceSpecs");

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecs_CustomerId",
                table: "AcceptanceSpecs",
                column: "CustomerId");

            migrationBuilder.DropIndex(
                name: "IX_AcceptanceSpecs_CustomerId_ProcessId",
                table: "AcceptanceSpecs");

            migrationBuilder.AlterColumn<int>(
                name: "ProcessId",
                table: "AcceptanceSpecs",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "MachineModelId",
                table: "AcceptanceSpecs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MachineModels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineModels", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecs_CustomerId_ProcessId_MachineModelId",
                table: "AcceptanceSpecs",
                columns: new[] { "CustomerId", "ProcessId", "MachineModelId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecs_MachineModelId",
                table: "AcceptanceSpecs",
                column: "MachineModelId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineModels_Name",
                table: "MachineModels",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_AcceptanceSpecs_MachineModels_MachineModelId",
                table: "AcceptanceSpecs",
                column: "MachineModelId",
                principalTable: "MachineModels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceptanceSpecs_Customers_CustomerId",
                table: "AcceptanceSpecs",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.DropTable(
                name: "MachineModels");

            migrationBuilder.DropIndex(
                name: "IX_AcceptanceSpecs_CustomerId_ProcessId_MachineModelId",
                table: "AcceptanceSpecs");

            migrationBuilder.DropIndex(
                name: "IX_AcceptanceSpecs_MachineModelId",
                table: "AcceptanceSpecs");

            migrationBuilder.DropColumn(
                name: "MachineModelId",
                table: "AcceptanceSpecs");

            migrationBuilder.AlterColumn<int>(
                name: "ProcessId",
                table: "AcceptanceSpecs",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_AcceptanceSpecs_CustomerId",
                table: "AcceptanceSpecs");

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecs_CustomerId_ProcessId",
                table: "AcceptanceSpecs",
                columns: new[] { "CustomerId", "ProcessId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AcceptanceSpecs_Customers_CustomerId",
                table: "AcceptanceSpecs",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
