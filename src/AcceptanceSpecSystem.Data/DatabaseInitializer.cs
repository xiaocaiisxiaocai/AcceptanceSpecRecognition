using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data;

/// <summary>
/// 数据库初始化服务
/// </summary>
public static class DatabaseInitializer
{
    public const string ControlledCollationMigrationId = "20260711010000_EnforceDatabaseCollation";
    public const int ControlledMigrationCommandTimeoutSeconds = 1800;
    private static readonly HashSet<string> DestructiveMigrationIds = new(StringComparer.Ordinal)
    {
        "20260317045437_RemoveOperationHistory",
        "20260113064729_RefactorSpecSetModel",
        "20260121190000_RemoveAiServiceConfigIsDefault",
        "20260318030725_AddRbacOrganizationModel",
        "20260318042111_AddAcceptanceSpecOwnership",
        "20260325093000_EnforceSingleRolePerUser",
        "20260325113000_EnforceSingleOrgPerUser",
        "20260327091604_RemoveLegacyTextProcessingTables",
        "20260415084722_RemovePromptTemplateIsDefault",
        "20260416093000_RepairLegacyAiServicePurposeAndTaskOwnership",
        "20260417012650_RemoveLegacyColumnMappingRules",
        "20260522005647_AddEmbeddingCacheUsageAndTextHash",
        ControlledCollationMigrationId,
        "20260719170000_BackfillDocumentTemplateRegions",
        "20260719190000_AddColumnMappingRuleNormalizedUniqueKey",
        "20260720120000_EnforceGlobalColumnMappingPatternIdentity",
        "20260720123000_HardenDocumentImportExecutions",
        "20260728090000_RestoreDocumentTemplateFingerprintUniqueIndex",
        "20260730120000_RemoveRedundantCustomerLearnedColumnRules",
        "20260731143000_MoveOperationalDataToElectricalControlDepartment",
    };
    private static readonly HashSet<string> SafeMigrationIds = new(StringComparer.Ordinal)
    {
        "20260113003102_InitialCreate_MySQL",
        "20260113040000_AddWordFilePath",
        "20260114025147_AddColumnMappingRules",
        "20260119022352_AddUploadedFileTypeToWordFile",
        "20260121084154_AddAiServicePurposePriority",
        "20260122071527_AddMachineModelAndOptionalProcess",
        "20260317013240_AddSystemUsers",
        "20260317025007_AddAuditLogs",
        "20260317102619_AddMatchingFillTasks",
        "20260320054700_AddAiServiceDisableThinking",
        "20260323032939_AddEmbeddingCacheExpiration",
        "20260325025444_AddPromptTemplateSceneMetadata",
        "20260326093813_FixReviewRound3",
        "20260328080402_AddWordFileOwnershipMetadata",
        "20260330035807_AddAiServiceMatchingDefaults",
        "20260402012744_AddExecutionHistoryRecords",
        "20260417021735_RestoreWordColumnMappingRules",
        "20260427012128_AddAiServiceDisabledState",
        "20260522092409_AddEmbeddingCacheWarmupSettings",
        "20260522094526_AddDatabaseBackupSettings",
        "20260602033630_AddAcceptanceSpecImportedAtIndex",
        "20260702063418_AddDocumentTemplate",
        "20260702064619_AddColumnMappingRuleLearning",
        "20260706090000_AddDocumentTemplateRoutingMetadata",
        "20260706093000_AddSmartStructureRoutingRules",
        "20260710144805_AddAuthRefreshSessions",
        "20260719074136_AddDocumentTemplateRegions",
        "20260719151435_AddDocumentImportExecutions",
        "20260724024559_OptimizeAcceptanceSpecGroupPagingIndex",
        "20260726142603_AddRowVersionToAiServiceConfig",
        "20260726163227_RestrictAcceptanceSpecReferenceDeletes",
        "20260727090000_AddWordFilePendingDeletion",
        "20260728040222_AddAcceptanceSpecUpdatedAt",
        "20260731064919_AddExecutionHistoryBusinessOrg",
        "20260805032114_AddAcceptanceSpecReferenceCount",
        "20260806065524_AddAcceptanceSpecReferenceHistory",
        "20260811033921_AddAcceptanceSpecContentVersionHistory"
    };

    /// <summary>
    /// 初始化数据库（应用所有待执行的迁移）
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <param name="allowControlledMigrations">是否由维护窗口显式允许受控迁移</param>
    /// <param name="backupVerified">是否已在本次维护窗口验证可恢复备份</param>
    /// <returns>初始化是否成功</returns>
    public static async Task<bool> InitializeAsync(
        AppDbContext context,
        bool allowControlledMigrations = false,
        bool backupVerified = false)
    {
        using var commandTimeoutScope = CreateControlledMigrationCommandTimeoutScope(
            context,
            allowControlledMigrations);

        try
        {
            // 获取待执行的迁移
            var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToArray();
            var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();

            EnsureControlledMigrationPolicy(
                appliedMigrations,
                pendingMigrations,
                allowControlledMigrations,
                backupVerified);

            if (pendingMigrations.Any())
            {
                // 应用所有待执行的迁移
                await context.Database.MigrateAsync();
            }

            return true;
        }
        catch (Exception)
        {
            // 迁移失败，可以在调用处捕获并处理
            throw;
        }
    }

    /// <summary>
    /// 初始化数据库（同步版本）
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <param name="allowControlledMigrations">是否由维护窗口显式允许受控迁移</param>
    /// <param name="backupVerified">是否已在本次维护窗口验证可恢复备份</param>
    /// <returns>初始化是否成功</returns>
    public static bool Initialize(
        AppDbContext context,
        bool allowControlledMigrations = false,
        bool backupVerified = false)
    {
        using var commandTimeoutScope = CreateControlledMigrationCommandTimeoutScope(
            context,
            allowControlledMigrations);

        try
        {
            var pendingMigrations = context.Database.GetPendingMigrations().ToArray();
            var appliedMigrations = context.Database.GetAppliedMigrations().ToArray();

            EnsureControlledMigrationPolicy(
                appliedMigrations,
                pendingMigrations,
                allowControlledMigrations,
                backupVerified);

            if (pendingMigrations.Any())
            {
                context.Database.Migrate();
            }

            return true;
        }
        catch (Exception)
        {
            throw;
        }
    }

    internal static IDisposable? CreateControlledMigrationCommandTimeoutScope(
        AppDbContext context,
        bool allowControlledMigrations)
    {
        if (!allowControlledMigrations)
        {
            return null;
        }

        var originalCommandTimeout = context.Database.GetCommandTimeout();
        context.Database.SetCommandTimeout(ControlledMigrationCommandTimeoutSeconds);
        return new CommandTimeoutScope(context, originalCommandTimeout);
    }

    private sealed class CommandTimeoutScope(
        AppDbContext context,
        int? originalCommandTimeout) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            context.Database.SetCommandTimeout(originalCommandTimeout);
            _disposed = true;
        }
    }

    public static void EnsureControlledMigrationPolicy(
        IReadOnlyCollection<string> appliedMigrations,
        IReadOnlyCollection<string> pendingMigrations,
        bool allowControlledMigrations,
        bool backupVerified = false)
    {
        // 全新空库不存在历史数据和在线 DDL 风险，可一次性建立最终结构。
        var existingDatabaseUpgrade = appliedMigrations.Count > 0;
        var destructiveMigrations = pendingMigrations
            .Where(migration => ClassifyMigration(migration) != DatabaseMigrationRisk.Safe)
            .ToArray();
        if (!existingDatabaseUpgrade || destructiveMigrations.Length == 0)
        {
            return;
        }

        if (!allowControlledMigrations)
        {
            throw new ControlledDatabaseMigrationRequiredException(
                $"检测到破坏性迁移 {string.Join(", ", destructiveMigrations)}。API 启动不会自动执行；" +
                "请先备份并完成恢复验证、停止 API 副本，再使用同一镜像执行 " +
                "--apply-destructive-migrations --backup-verified，完成后再启动服务。");
        }

        if (!backupVerified)
        {
            throw new ControlledDatabaseMigrationRequiredException(
                $"破坏性迁移 {string.Join(", ", destructiveMigrations)} 尚未确认备份恢复验证；" +
                "仅在备份已验证后追加 --backup-verified。");
        }
    }

    public static DatabaseMigrationRisk ClassifyMigration(string migrationId)
    {
        if (DestructiveMigrationIds.Contains(migrationId))
            return DatabaseMigrationRisk.Destructive;
        return SafeMigrationIds.Contains(migrationId)
            ? DatabaseMigrationRisk.Safe
            : DatabaseMigrationRisk.Unclassified;
    }

    /// <summary>
    /// 确保数据库已创建（不使用迁移，直接创建）
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <returns>数据库是否已创建</returns>
    public static async Task<bool> EnsureCreatedAsync(AppDbContext context)
    {
        return await context.Database.EnsureCreatedAsync();
    }

    /// <summary>
    /// 检查数据库是否可以连接
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <returns>是否可以连接</returns>
    public static async Task<bool> CanConnectAsync(AppDbContext context)
    {
        return await context.Database.CanConnectAsync();
    }

    /// <summary>
    /// 获取已应用的迁移列表
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <returns>已应用的迁移名称列表</returns>
    public static async Task<IEnumerable<string>> GetAppliedMigrationsAsync(AppDbContext context)
    {
        return await context.Database.GetAppliedMigrationsAsync();
    }

    /// <summary>
    /// 获取待执行的迁移列表
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <returns>待执行的迁移名称列表</returns>
    public static async Task<IEnumerable<string>> GetPendingMigrationsAsync(AppDbContext context)
    {
        return await context.Database.GetPendingMigrationsAsync();
    }
}

public sealed class ControlledDatabaseMigrationRequiredException(string message) : InvalidOperationException(message);

public enum DatabaseMigrationRisk
{
    Safe,
    Destructive,
    Unclassified
}
