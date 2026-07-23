using System;
using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260706090000_AddDocumentTemplateRoutingMetadata")]
    public partial class AddDocumentTemplateRoutingMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAt",
                table: "DocumentTemplates",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Recommendation",
                table: "DocumentTemplates",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "NeedConfirm")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "TableKind",
                table: "DocumentTemplates",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Unknown")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "UserModifiedStructure",
                table: "DocumentTemplates",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "DocumentTemplates");

            migrationBuilder.DropColumn(
                name: "Recommendation",
                table: "DocumentTemplates");

            migrationBuilder.DropColumn(
                name: "TableKind",
                table: "DocumentTemplates");

            migrationBuilder.DropColumn(
                name: "UserModifiedStructure",
                table: "DocumentTemplates");
        }
    }
}
