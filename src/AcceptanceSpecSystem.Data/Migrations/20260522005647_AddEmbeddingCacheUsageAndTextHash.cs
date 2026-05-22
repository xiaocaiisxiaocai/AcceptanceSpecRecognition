using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddingCacheUsageAndTextHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TextHash",
                table: "EmbeddingCaches",
                type: "varchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Usage",
                table: "EmbeddingCaches",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "matching")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EmbeddingCaches_SpecId_ModelName_Usage",
                table: "EmbeddingCaches",
                columns: new[] { "SpecId", "ModelName", "Usage" },
                unique: true);

            // MySQL 外键要求引用列始终有可用索引；先建新索引，再删旧索引。
            migrationBuilder.DropIndex(
                name: "IX_EmbeddingCaches_SpecId_ModelName",
                table: "EmbeddingCaches");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM `EmbeddingCaches` WHERE `Usage` <> 'matching';");

            migrationBuilder.CreateIndex(
                name: "IX_EmbeddingCaches_SpecId_ModelName",
                table: "EmbeddingCaches",
                columns: new[] { "SpecId", "ModelName" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_EmbeddingCaches_SpecId_ModelName_Usage",
                table: "EmbeddingCaches");

            migrationBuilder.DropColumn(
                name: "TextHash",
                table: "EmbeddingCaches");

            migrationBuilder.DropColumn(
                name: "Usage",
                table: "EmbeddingCaches");
        }
    }
}
