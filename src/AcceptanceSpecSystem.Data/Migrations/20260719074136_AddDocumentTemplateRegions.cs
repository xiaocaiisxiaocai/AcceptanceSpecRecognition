using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTemplateRegions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTemplateRegions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    DocumentTemplateId = table.Column<int>(type: "int", nullable: false),
                    RegionIndex = table.Column<int>(type: "int", nullable: false),
                    HeadersJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HeaderRowIndex = table.Column<int>(type: "int", nullable: false),
                    HeaderRowCount = table.Column<int>(type: "int", nullable: false),
                    DataStartRowIndex = table.Column<int>(type: "int", nullable: false),
                    DataEndRowIndex = table.Column<int>(type: "int", nullable: true),
                    ProjectColumnIndex = table.Column<int>(type: "int", nullable: true),
                    SpecificationColumnIndex = table.Column<int>(type: "int", nullable: false),
                    AcceptanceColumnIndex = table.Column<int>(type: "int", nullable: true),
                    RemarkColumnIndex = table.Column<int>(type: "int", nullable: true),
                    IsSpecificationOnly = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTemplateRegions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentTemplateRegions_DocumentTemplates_DocumentTemplateId",
                        column: x => x.DocumentTemplateId,
                        principalTable: "DocumentTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplateRegions_DocumentTemplateId_RegionIndex",
                table: "DocumentTemplateRegions",
                columns: new[] { "DocumentTemplateId", "RegionIndex" },
                unique: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTemplateRegions");
        }
    }
}
