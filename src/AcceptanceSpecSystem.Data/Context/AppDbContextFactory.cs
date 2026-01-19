using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AcceptanceSpecSystem.Data.Context;

/// <summary>
/// 设计时DbContext工厂，用于EF Core迁移命令
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <summary>
    /// 创建DbContext实例
    /// </summary>
    /// <param name="args">命令行参数</param>
    /// <returns>DbContext实例</returns>
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        var connectionString = AppDbContext.DefaultConnectionString;
        optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));

        return new AppDbContext(optionsBuilder.Options);
    }
}
