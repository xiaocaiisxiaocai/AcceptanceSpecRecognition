using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiServiceMatchingDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultMatchingStrategy",
                table: "AiServiceConfigs",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "DefaultRecallTopK",
                table: "AiServiceConfigs",
                type: "int",
                nullable: false,
                defaultValue: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultMatchingStrategy",
                table: "AiServiceConfigs");

            migrationBuilder.DropColumn(
                name: "DefaultRecallTopK",
                table: "AiServiceConfigs");
        }
    }
}
