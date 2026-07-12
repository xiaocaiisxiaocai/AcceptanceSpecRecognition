using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data;

/// <summary>
/// 数据库初始化服务
/// </summary>
public static class DatabaseInitializer
{
    public const string ControlledCollationMigrationId = "20260711010000_EnforceDatabaseCollation";

    /// <summary>
    /// 初始化数据库（应用所有待执行的迁移）
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <param name="allowControlledMigrations">是否由维护窗口显式允许受控迁移</param>
    /// <returns>初始化是否成功</returns>
    public static async Task<bool> InitializeAsync(
        AppDbContext context,
        bool allowControlledMigrations = false)
    {
        try
        {
            // 获取待执行的迁移
            var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToArray();
            var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();

            EnsureControlledMigrationPolicy(appliedMigrations, pendingMigrations, allowControlledMigrations);

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
    /// <returns>初始化是否成功</returns>
    public static bool Initialize(AppDbContext context, bool allowControlledMigrations = false)
    {
        try
        {
            var pendingMigrations = context.Database.GetPendingMigrations().ToArray();
            var appliedMigrations = context.Database.GetAppliedMigrations().ToArray();

            EnsureControlledMigrationPolicy(appliedMigrations, pendingMigrations, allowControlledMigrations);

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

    public static void EnsureControlledMigrationPolicy(
        IReadOnlyCollection<string> appliedMigrations,
        IReadOnlyCollection<string> pendingMigrations,
        bool allowControlledMigrations)
    {
        // 全新空库不存在历史数据和在线 DDL 风险，可一次性建立最终结构。
        var existingDatabaseUpgrade = appliedMigrations.Count > 0;
        if (existingDatabaseUpgrade &&
            !allowControlledMigrations &&
            pendingMigrations.Contains(ControlledCollationMigrationId, StringComparer.Ordinal))
        {
            throw new ControlledDatabaseMigrationRequiredException(
                $"检测到受控迁移 {ControlledCollationMigrationId}。API 启动不会自动执行全表排序规则重写；" +
                "请先备份并停止 API 副本，再使用同一镜像执行 --migrate-only，完成后再启动服务。");
        }
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
