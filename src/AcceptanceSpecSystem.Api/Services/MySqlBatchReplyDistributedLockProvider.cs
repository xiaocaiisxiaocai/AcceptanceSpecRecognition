using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class MySqlBatchReplyDistributedLockProvider(
    IServiceScopeFactory scopeFactory,
    ILogger<MySqlBatchReplyDistributedLockProvider> logger) : IBatchReplyDistributedLockProvider
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string key,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;
        if (!string.Equals(database.ProviderName, "Pomelo.EntityFrameworkCore.MySql", StringComparison.Ordinal))
        {
            // API 集成测试会显式替换为 SQLite；生产配置只允许 MySQL。
            scope.Dispose();
            return NoOpLease.Instance;
        }

        var connection = database.GetDbConnection();
        try
        {
            await connection.OpenAsync(cancellationToken);
            var lockMaterial = $"{connection.Database}\0{key}";
            var lockName = $"acceptance:batch:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lockMaterial)))[..32]}";
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT GET_LOCK(@lockName, @timeoutSeconds);";
            AddParameter(command, "@lockName", lockName);
            AddParameter(command, "@timeoutSeconds", Math.Max(0, (int)Math.Ceiling(waitTimeout.TotalSeconds)));
            var acquired = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
            if (!acquired)
            {
                await connection.DisposeAsync();
                scope.Dispose();
                return null;
            }

            return new Lease(connection, scope, lockName, logger);
        }
        catch
        {
            await connection.DisposeAsync();
            scope.Dispose();
            throw;
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed class NoOpLease : IAsyncDisposable
    {
        public static NoOpLease Instance { get; } = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Lease(
        DbConnection connection,
        IServiceScope scope,
        string lockName,
        ILogger logger) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT RELEASE_LOCK(@lockName);";
                AddParameter(command, "@lockName", lockName);
                await command.ExecuteScalarAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "显式释放 BatchReply MySQL 锁失败；连接关闭后服务端会自动释放");
            }
            finally
            {
                await connection.DisposeAsync();
                scope.Dispose();
            }
        }
    }
}
