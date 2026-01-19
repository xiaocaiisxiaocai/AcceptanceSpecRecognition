using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <summary>
    /// 数据库迁移：新增导入列映射规则表（ColumnMappingRules）。
    /// </summary>
    public partial class AddColumnMappingRules : Migration
    {
        /// <summary>
        /// 应用迁移：创建 ColumnMappingRules 表与相关索引。
        /// </summary>
        /// <param name="migrationBuilder">迁移构建器</param>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ColumnMappingRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TargetField = table.Column<int>(type: "int", nullable: false),
                    MatchMode = table.Column<int>(type: "int", nullable: false),
                    Pattern = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColumnMappingRules", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ColumnMappingRules_TargetField_Pattern",
                table: "ColumnMappingRules",
                columns: new[] { "TargetField", "Pattern" });

            migrationBuilder.CreateIndex(
                name: "IX_ColumnMappingRules_TargetField_Priority",
                table: "ColumnMappingRules",
                columns: new[] { "TargetField", "Priority" });
        }

        /// <summary>
        /// 回滚迁移：删除 ColumnMappingRules 表。
        /// </summary>
        /// <param name="migrationBuilder">迁移构建器</param>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ColumnMappingRules");
        }
    }
}
