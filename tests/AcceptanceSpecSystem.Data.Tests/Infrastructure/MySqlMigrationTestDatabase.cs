using System.Data;
using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace AcceptanceSpecSystem.Data.Tests.Infrastructure;

/// <summary>
/// 真实 MySQL 迁移烟测的隔离测试库。
/// </summary>
public sealed class MySqlMigrationTestDatabase : IAsyncDisposable
{
    public const string EnableEnvironmentVariableName = "ACCEPTANCE_SPEC_ENABLE_MYSQL_MIGRATION_SMOKE_TESTS";
    public const string BaseConnectionEnvironmentVariableName = "ACCEPTANCE_SPEC_MYSQL_MIGRATION_BASE_CONNECTION";

    private readonly string _adminConnectionString;
    private bool _disposed;

    private MySqlMigrationTestDatabase(string adminConnectionString, string connectionString, string databaseName)
    {
        _adminConnectionString = adminConnectionString;
        ConnectionString = connectionString;
        DatabaseName = databaseName;
    }

    /// <summary>
    /// 测试库名，格式：acceptance_spec_test_yyyyMMddHHmmss_8位随机后缀。
    /// </summary>
    public string DatabaseName { get; }

    /// <summary>
    /// 测试库连接串。
    /// </summary>
    public string ConnectionString { get; }

    public static async Task<MySqlMigrationTestDatabase> CreateAsync()
    {
        var baseConnectionString = ReadBaseConnectionString();
        var databaseName = BuildDatabaseName();

        var adminBuilder = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Database = "mysql",
            Pooling = false
        };
        var databaseBuilder = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName,
            Pooling = false
        };

        await using var adminConnection = new MySqlConnection(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();
        await using (var command = adminConnection.CreateCommand())
        {
            command.CommandText =
                $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await command.ExecuteNonQueryAsync();
        }

        return new MySqlMigrationTestDatabase(
            adminBuilder.ConnectionString,
            databaseBuilder.ConnectionString,
            databaseName);
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString))
            .Options;

        return new AppDbContext(options);
    }

    public async Task<object?> ExecuteScalarAsync(string sql)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    public async Task<DataTable> QueryAsync(string sql)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await using var adminConnection = new MySqlConnection(_adminConnectionString);
        await adminConnection.OpenAsync();
        await using var command = adminConnection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{DatabaseName}`;";
        await command.ExecuteNonQueryAsync();
    }

    private static string BuildDatabaseName()
    {
        return $"acceptance_spec_test_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..45];
    }

    private static string ReadBaseConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(BaseConnectionEnvironmentVariableName)?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"未设置 {BaseConnectionEnvironmentVariableName}，无法创建真实 MySQL 迁移测试库。");
        }

        return connectionString;
    }
}
