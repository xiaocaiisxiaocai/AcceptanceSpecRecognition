using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddingCacheExpiration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "EmbeddingCaches",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelVersion",
                table: "EmbeddingCaches",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EmbeddingCaches_ExpiresAt",
                table: "EmbeddingCaches",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmbeddingCaches_ExpiresAt",
                table: "EmbeddingCaches");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "EmbeddingCaches");

            migrationBuilder.DropColumn(
                name: "ModelVersion",
                table: "EmbeddingCaches");
        }
    }
}
