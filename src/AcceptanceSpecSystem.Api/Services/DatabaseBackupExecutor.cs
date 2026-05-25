using System.Data.Common;
using System.Diagnostics;
using System.IO.Compression;
using AcceptanceSpecSystem.Api.Options;

namespace AcceptanceSpecSystem.Api.Services;

public interface IDatabaseBackupExecutor
{
    Task<DatabaseBackupExecutionResult> BackupAsync(
        DatabaseBackupOptions options,
        CancellationToken cancellationToken);
}

public sealed record DatabaseBackupExecutionResult(string FileName, long FileSizeBytes);

/// <summary>
/// 基于 mysqldump 的 MySQL 备份执行器。
/// </summary>
public sealed class MySqlDumpDatabaseBackupExecutor : IDatabaseBackupExecutor
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MySqlDumpDatabaseBackupExecutor> _logger;

    public MySqlDumpDatabaseBackupExecutor(
        IConfiguration configuration,
        ILogger<MySqlDumpDatabaseBackupExecutor> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<DatabaseBackupExecutionResult> BackupAsync(
        DatabaseBackupOptions options,
        CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection")?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection 未配置，无法备份数据库。");
        }

        var connection = ParseConnection(connectionString);
        Directory.CreateDirectory(options.BackupDirectory);

        var fileName = $"{SanitizeFileName(connection.Database)}-{DateTime.UtcNow:yyyyMMddHHmmss}.sql.gz";
        var filePath = Path.Combine(options.BackupDirectory, fileName);

        var startInfo = new ProcessStartInfo
        {
            FileName = "mysqldump",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add($"--host={connection.Host}");
        startInfo.ArgumentList.Add($"--port={connection.Port}");
        startInfo.ArgumentList.Add($"--user={connection.User}");
        startInfo.ArgumentList.Add("--single-transaction");
        startInfo.ArgumentList.Add("--quick");
        startInfo.ArgumentList.Add("--routines");
        startInfo.ArgumentList.Add("--events");
        startInfo.ArgumentList.Add("--default-character-set=utf8mb4");
        startInfo.ArgumentList.Add(connection.Database);

        if (!string.IsNullOrEmpty(connection.Password))
        {
            startInfo.Environment["MYSQL_PWD"] = connection.Password;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 mysqldump，请确认容器内已安装 MySQL 客户端。");

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await using (var fileStream = File.Create(filePath))
        await using (var gzipStream = new GZipStream(fileStream, CompressionLevel.SmallestSize))
        {
            await process.StandardOutput.BaseStream.CopyToAsync(gzipStream, cancellationToken);
        }

        await process.WaitForExitAsync(cancellationToken);
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            TryDeleteFile(filePath);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr)
                    ? $"mysqldump 退出码 {process.ExitCode}"
                    : stderr.Trim());
        }

        CleanupOldBackups(options.BackupDirectory, $"{SanitizeFileName(connection.Database)}-*.sql.gz", options.RetentionCount);
        var fileInfo = new FileInfo(filePath);
        _logger.LogInformation("数据库备份完成：{FileName}, Size={SizeBytes}", fileInfo.Name, fileInfo.Length);
        return new DatabaseBackupExecutionResult(fileInfo.Name, fileInfo.Length);
    }

    private static MySqlConnectionInfo ParseConnection(string connectionString)
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };

        var host = GetValue(builder, "Server", "Host", "Data Source", "Address") ?? "localhost";
        var portText = GetValue(builder, "Port");
        var database = GetValue(builder, "Database", "Initial Catalog");
        var user = GetValue(builder, "User", "User ID", "Uid", "Username");
        var password = GetValue(builder, "Password", "Pwd");

        if (string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("数据库连接串缺少 Database，无法备份。");
        if (string.IsNullOrWhiteSpace(user))
            throw new InvalidOperationException("数据库连接串缺少 User，无法备份。");

        return new MySqlConnectionInfo(
            Host: host,
            Port: int.TryParse(portText, out var port) ? port : 3306,
            Database: database,
            User: user,
            Password: password ?? string.Empty);
    }

    private static string? GetValue(DbConnectionStringBuilder builder, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (builder.TryGetValue(key, out var value))
            {
                return value?.ToString();
            }
        }

        return null;
    }

    private static void CleanupOldBackups(string backupDirectory, string searchPattern, int retentionCount)
    {
        var keepCount = Math.Max(1, retentionCount);
        foreach (var file in new DirectoryInfo(backupDirectory)
                     .EnumerateFiles(searchPattern)
                     .OrderByDescending(file => file.CreationTimeUtc)
                     .Skip(keepCount))
        {
            TryDeleteFile(file.FullName);
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
            // 清理失败不影响主流程，调用方会记录备份结果。
        }
    }

    private sealed record MySqlConnectionInfo(
        string Host,
        int Port,
        string Database,
        string User,
        string Password);
}
