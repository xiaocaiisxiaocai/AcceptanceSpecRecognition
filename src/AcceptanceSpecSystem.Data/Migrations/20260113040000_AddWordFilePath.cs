using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations;

/// <summary>
/// 数据库迁移：为 <c>WordFiles</c> 增加 <c>FilePath</c> 字段。
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260113040000_AddWordFilePath")]
public partial class AddWordFilePath : Migration
{
    /// <summary>
    /// 应用迁移：新增 <c>WordFiles.FilePath</c> 列。
    /// </summary>
    /// <param name="migrationBuilder">迁移构建器</param>
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FilePath",
            table: "WordFiles",
            type: "varchar(500)",
            maxLength: 500,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");
    }

    /// <summary>
    /// 回滚迁移：删除 <c>WordFiles.FilePath</c> 列。
    /// </summary>
    /// <param name="migrationBuilder">迁移构建器</param>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "FilePath",
            table: "WordFiles");
    }
}

