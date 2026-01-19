using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data;

/// <summary>
/// 数据库初始化服务
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>
    /// 初始化数据库（应用所有待执行的迁移）
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <returns>初始化是否成功</returns>
    public static async Task<bool> InitializeAsync(AppDbContext context)
    {
        try
        {
            // 获取待执行的迁移
            var pendingMigrations = await context.Database.GetPendingMigrationsAsync();

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
    /// <returns>初始化是否成功</returns>
    public static bool Initialize(AppDbContext context)
    {
        try
        {
            var pendingMigrations = context.Database.GetPendingMigrations();

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
