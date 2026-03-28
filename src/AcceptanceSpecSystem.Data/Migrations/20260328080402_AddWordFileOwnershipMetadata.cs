using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWordFileOwnershipMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WordFiles_FileHash",
                table: "WordFiles");

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "WordFiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "WordFiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerOrgUnitId",
                table: "WordFiles",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WordFiles_CompanyId",
                table: "WordFiles",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_WordFiles_CreatedByUserId",
                table: "WordFiles",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WordFiles_FileHash",
                table: "WordFiles",
                column: "FileHash");

            migrationBuilder.CreateIndex(
                name: "IX_WordFiles_OwnerOrgUnitId",
                table: "WordFiles",
                column: "OwnerOrgUnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WordFiles_CompanyId",
                table: "WordFiles");

            migrationBuilder.DropIndex(
                name: "IX_WordFiles_CreatedByUserId",
                table: "WordFiles");

            migrationBuilder.DropIndex(
                name: "IX_WordFiles_FileHash",
                table: "WordFiles");

            migrationBuilder.DropIndex(
                name: "IX_WordFiles_OwnerOrgUnitId",
                table: "WordFiles");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "WordFiles");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "WordFiles");

            migrationBuilder.DropColumn(
                name: "OwnerOrgUnitId",
                table: "WordFiles");

            migrationBuilder.CreateIndex(
                name: "IX_WordFiles_FileHash",
                table: "WordFiles",
                column: "FileHash",
                unique: true);
        }
    }
}
