using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAcceptanceSpecReferenceCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ReferenceCount",
                table: "AcceptanceSpecs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReferenceCount",
                table: "AcceptanceSpecs");
        }
    }
}
