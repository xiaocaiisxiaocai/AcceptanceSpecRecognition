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

        // MySQL DDL 会隐式提交，不能依赖 EF 事务回滚。进度表使维护窗口中断后可从
        // 未完成表继续，避免对已经成功转换的大表重复执行昂贵的 ALTER TABLE。
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS `__ControlledMigrationProgress` (
                `MigrationId` varchar(150) NOT NULL,
                `ObjectName` varchar(150) NOT NULL,
                `CompletedAt` datetime(6) NOT NULL,
                PRIMARY KEY (`MigrationId`, `ObjectName`)
            ) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

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
                $"""
                SET @controlled_ddl = IF(
                    EXISTS(
                        SELECT 1 FROM `__ControlledMigrationProgress`
                        WHERE `MigrationId` = '20260711010000_EnforceDatabaseCollation'
                          AND `ObjectName` = '{table}'
                    ),
                    'SELECT 1',
                    'ALTER TABLE `{table}` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci'
                );
                PREPARE controlled_stmt FROM @controlled_ddl;
                EXECUTE controlled_stmt;
                DEALLOCATE PREPARE controlled_stmt;
                INSERT IGNORE INTO `__ControlledMigrationProgress` (`MigrationId`, `ObjectName`, `CompletedAt`)
                VALUES ('20260711010000_EnforceDatabaseCollation', '{table}', UTC_TIMESTAMP(6));
                """);
        }

        // 保留极小的进度表：若所有 ALTER 已完成但 EF 写迁移历史时进程退出，
        // 下一次维护模式运行仍能跳过已经完成的全表转换。
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // 迁移前的数据库排序规则取决于服务器版本和历史部署，无法安全推断。
        // 回滚代码版本时保留兼容性更好的 utf8mb4_unicode_ci，避免破坏已有索引语义。
    }
}
