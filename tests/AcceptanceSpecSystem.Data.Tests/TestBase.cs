using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Tests;

/// <summary>
/// 测试基类，提供内存数据库上下文
/// </summary>
public abstract class TestBase : IDisposable
{
    /// <summary>
    /// 数据库上下文
    /// </summary>
    protected AppDbContext Context { get; }

    /// <summary>
    /// 创建测试基类实例
    /// </summary>
    protected TestBase()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        Context = new AppDbContext(options);
        Context.Database.EnsureCreated();
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        Context.Database.EnsureDeleted();
        Context.Dispose();
        GC.SuppressFinalize(this);
    }
}
