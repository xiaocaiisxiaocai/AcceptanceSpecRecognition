using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <summary>
    /// 数据库迁移：重构“验规集”模型（Customer 与 Process 解耦，AcceptanceSpecs 直接关联 CustomerId + ProcessId）。
    /// </summary>
    public partial class RefactorSpecSetModel : Migration
    {
        /// <summary>
        /// 应用迁移：为 AcceptanceSpecs 增加 CustomerId 并回填数据，建立索引/外键，同时移除 Processes.CustomerId 关系。
        /// </summary>
        /// <param name="migrationBuilder">迁移构建器</param>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) 为 AcceptanceSpecs 增加 CustomerId（先允许为空，后续回填再改为非空）
            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "AcceptanceSpecs",
                type: "int",
                nullable: true);

            // 2) 数据迁移：通过旧关系 AcceptanceSpecs.ProcessId -> Processes.CustomerId 回填
            migrationBuilder.Sql("""
                UPDATE AcceptanceSpecs a
                INNER JOIN Processes p ON a.ProcessId = p.Id
                SET a.CustomerId = p.CustomerId
                WHERE a.CustomerId IS NULL;
                """);

            // 3) 改为非空（如果历史数据存在异常，这一步会失败；请先清理数据再迁移）
            migrationBuilder.AlterColumn<int>(
                name: "CustomerId",
                table: "AcceptanceSpecs",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            // 4) 建索引 + 外键
            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecs_CustomerId_ProcessId",
                table: "AcceptanceSpecs",
                columns: new[] { "CustomerId", "ProcessId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AcceptanceSpecs_Customers_CustomerId",
                table: "AcceptanceSpecs",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // 5) 移除 Processes -> Customers 的从属关系（最后再 drop，确保回填 SQL 可用）
            migrationBuilder.DropForeignKey(
                name: "FK_Processes_Customers_CustomerId",
                table: "Processes");

            migrationBuilder.DropIndex(
                name: "IX_Processes_CustomerId_Name",
                table: "Processes");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "Processes");

            migrationBuilder.CreateIndex(
                name: "IX_Processes_Name",
                table: "Processes",
                column: "Name");
        }

        /// <summary>
        /// 回滚迁移：恢复 Processes.CustomerId 关系并移除 AcceptanceSpecs.CustomerId 相关变更。
        /// </summary>
        /// <param name="migrationBuilder">迁移构建器</param>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 回滚顺序：先恢复 Processes.CustomerId，再移除 AcceptanceSpecs.CustomerId

            migrationBuilder.DropForeignKey(
                name: "FK_AcceptanceSpecs_Customers_CustomerId",
                table: "AcceptanceSpecs");

            migrationBuilder.DropIndex(
                name: "IX_Processes_Name",
                table: "Processes");

            migrationBuilder.DropIndex(
                name: "IX_AcceptanceSpecs_CustomerId_ProcessId",
                table: "AcceptanceSpecs");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "AcceptanceSpecs");

            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "Processes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Processes_CustomerId_Name",
                table: "Processes",
                columns: new[] { "CustomerId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Processes_Customers_CustomerId",
                table: "Processes",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
