using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations;

/// <inheritdoc />
public partial class RemoveAiServiceConfigIsDefault : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsDefault",
            table: "AiServiceConfigs");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsDefault",
            table: "AiServiceConfigs",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: false);
    }
}
