using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260720123000_HardenDocumentImportExecutions")]
public sealed class HardenDocumentImportExecutions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "ExpiresAt",
            table: "DocumentImportExecutions",
            type: "datetime(6)",
            nullable: true);

        if (migrationBuilder.ActiveProvider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql(
                "UPDATE `DocumentImportExecutions` SET `ExpiresAt` = DATE_ADD(`CreatedAt`, INTERVAL 24 HOUR) WHERE `ExpiresAt` IS NULL;");
        }
        else
        {
            migrationBuilder.Sql(
                "UPDATE \"DocumentImportExecutions\" SET \"ExpiresAt\" = datetime(\"CreatedAt\", '+24 hours') WHERE \"ExpiresAt\" IS NULL;");
        }

        migrationBuilder.AlterColumn<DateTime>(
            name: "ExpiresAt",
            table: "DocumentImportExecutions",
            type: "datetime(6)",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "datetime(6)",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_DocumentImportExecutions_ExpiresAt",
            table: "DocumentImportExecutions",
            column: "ExpiresAt");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_DocumentImportExecutions_ExpiresAt",
            table: "DocumentImportExecutions");

        migrationBuilder.DropColumn(
            name: "ExpiresAt",
            table: "DocumentImportExecutions");
    }
}
