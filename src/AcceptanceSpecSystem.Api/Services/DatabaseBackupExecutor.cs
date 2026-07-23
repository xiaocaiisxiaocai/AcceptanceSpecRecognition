using System.Data.Common;
using System.Diagnostics;
using System.IO.Compression;

using AcceptanceSpecSystem.Application.Options;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 基于 mysqldump 的 MySQL 备份执行器。
/// </summary>
public sealed class MySqlDumpDatabaseBackupExecutor : IDatabaseBackupExecutor
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MySqlDumpDatabaseBackupExecutor> _logger;
    private readonly IMySqlDumpProcessRunner _processRunner;

    public MySqlDumpDatabaseBackupExecutor(
        IConfiguration configuration,
        ILogger<MySqlDumpDatabaseBackupExecutor> logger,
        IMySqlDumpProcessRunner processRunner)
    {
        _configuration = configuration;
        _logger = logger;
        _processRunner = processRunner;
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

        var fileName = $"{SanitizeFileName(connection.Database)}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.sql.gz";
        var filePath = Path.Combine(options.BackupDirectory, fileName);
        var partialPath = Path.Combine(options.BackupDirectory, $".{fileName}.{Guid.NewGuid():N}.partial");

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
        startInfo.ArgumentList.Add("--no-tablespaces");
        startInfo.ArgumentList.Add("--routines");
        startInfo.ArgumentList.Add("--events");
        startInfo.ArgumentList.Add("--default-character-set=utf8mb4");
        startInfo.ArgumentList.Add(connection.Database);

        if (!string.IsNullOrEmpty(connection.Password))
        {
            startInfo.Environment["MYSQL_PWD"] = connection.Password;
        }

        try
        {
            MySqlDumpProcessResult processResult;
            await using (var fileStream = new FileStream(
                             partialPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var gzipStream = new GZipStream(fileStream, CompressionLevel.SmallestSize))
            {
                processResult = await _processRunner.RunAsync(startInfo, gzipStream, cancellationToken);
            }

            if (processResult.ExitCode != 0 || !string.IsNullOrWhiteSpace(processResult.StandardError))
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(processResult.StandardError)
                        ? $"mysqldump 退出码 {processResult.ExitCode}"
                        : processResult.StandardError.Trim());
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partialPath, filePath);

            CleanupOldBackups(options.BackupDirectory, $"{SanitizeFileName(connection.Database)}-*.sql.gz", options.RetentionCount);
            var fileInfo = new FileInfo(filePath);
            _logger.LogInformation("数据库备份完成：{FileName}, Size={SizeBytes}", fileInfo.Name, fileInfo.Length);
            return new DatabaseBackupExecutionResult(fileInfo.Name, fileInfo.Length);
        }
        catch
        {
            TryDeleteFile(partialPath, _logger);
            throw;
        }
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

    private static void TryDeleteFile(string filePath, ILogger logger)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "清理未完成数据库备份失败: {FileName}", Path.GetFileName(filePath));
        }
    }

    private sealed record MySqlConnectionInfo(
        string Host,
        int Port,
        string Database,
        string User,
        string Password);
}

public sealed record MySqlDumpProcessResult(int ExitCode, string StandardError);

public interface IMySqlDumpProcessRunner
{
    Task<MySqlDumpProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        Stream standardOutputDestination,
        CancellationToken cancellationToken);
}

public sealed class MySqlDumpProcessRunner : IMySqlDumpProcessRunner
{
    public async Task<MySqlDumpProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        Stream standardOutputDestination,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 mysqldump，请确认容器内已安装 MySQL 客户端。");

        using var cancellationRegistration = cancellationToken.Register(() => TryKillProcessTree(process));
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(standardOutputDestination, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new MySqlDumpProcessResult(process.ExitCode, await stderrTask);
        }
        catch
        {
            TryKillProcessTree(process);
            await WaitForExitAfterFailureAsync(process);
            throw;
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private static async Task WaitForExitAfterFailureAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
        }
    }
}
