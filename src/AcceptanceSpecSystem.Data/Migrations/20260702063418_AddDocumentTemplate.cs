using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    TemplateName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HeadersFingerprint = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HeadersJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProjectColumnIndex = table.Column<int>(type: "int", nullable: true),
                    SpecificationColumnIndex = table.Column<int>(type: "int", nullable: false),
                    AcceptanceColumnIndex = table.Column<int>(type: "int", nullable: true),
                    RemarkColumnIndex = table.Column<int>(type: "int", nullable: true),
                    HeaderRowIndex = table.Column<int>(type: "int", nullable: false),
                    HeaderRowCount = table.Column<int>(type: "int", nullable: false),
                    DataStartRowIndex = table.Column<int>(type: "int", nullable: false),
                    DataEndRowIndex = table.Column<int>(type: "int", nullable: true),
                    IsSpecificationOnly = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UsageCount = table.Column<int>(type: "int", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentTemplates_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplates_CustomerId_HeadersFingerprint",
                table: "DocumentTemplates",
                columns: new[] { "CustomerId", "HeadersFingerprint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTemplates");
        }
    }
}
