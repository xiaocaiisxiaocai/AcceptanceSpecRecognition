using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations;

/// <summary>
/// 显式固定数据库默认排序规则，避免 MySQL 8 采用版本相关的服务器默认值。
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260711010000_EnforceDatabaseCollation")]
public sealed class EnforceDatabaseCollation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER DATABASE CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");

        // ALTER DATABASE 只影响后续新建对象。历史表可能已经继承 MySQL 8 的
        // utf8mb4_0900_ai_ci，因此必须显式转换当前模型中的所有表。
        string[] tables =
        [
            "__EFMigrationsHistory",
            "AcceptanceSpecs",
            "AiServiceConfigs",
            "AuditLogs",
            "AuthPermissions",
            "AuthRefreshSessions",
            "AuthRoleDataScopeNodes",
            "AuthRoleDataScopes",
            "AuthRolePermissions",
            "AuthRoles",
            "AuthUserOrgUnits",
            "AuthUserRoles",
            "ColumnMappingRules",
            "Customers",
            "DatabaseBackupSettings",
            "DocumentTemplates",
            "EmbeddingCaches",
            "EmbeddingCacheWarmupSettings",
            "ExecutionHistoryRecords",
            "MachineModels",
            "MatchingFillTasks",
            "OrgCompanies",
            "OrgUnits",
            "Processes",
            "PromptTemplates",
            "SmartStructureRoutingRules",
            "SystemUsers",
            "WordFiles"
        ];

        foreach (var table in tables)
        {
            migrationBuilder.Sql(
                $"ALTER TABLE `{table}` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // 迁移前的数据库排序规则取决于服务器版本和历史部署，无法安全推断。
        // 回滚代码版本时保留兼容性更好的 utf8mb4_unicode_ci，避免破坏已有索引语义。
    }
}
