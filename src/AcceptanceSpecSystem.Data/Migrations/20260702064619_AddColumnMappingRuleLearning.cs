using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddColumnMappingRuleLearning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "ColumnMappingRules",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "ColumnMappingRules",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.CreateIndex(
                name: "IX_ColumnMappingRules_CustomerId_TargetField_Pattern",
                table: "ColumnMappingRules",
                columns: new[] { "CustomerId", "TargetField", "Pattern" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ColumnMappingRules_CustomerId_TargetField_Pattern",
                table: "ColumnMappingRules");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "ColumnMappingRules");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ColumnMappingRules");
        }
    }
}
