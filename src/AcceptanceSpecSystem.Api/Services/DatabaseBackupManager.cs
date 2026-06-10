using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class DatabaseBackupManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseBackupManager> _logger;
    private readonly object _lock = new();
    private DatabaseBackupOptions _options;
    private DatabaseBackupStatusDto _status = new();
    private bool _persistedOptionsLoaded;

    public DatabaseBackupManager(
        IServiceScopeFactory scopeFactory,
        IOptions<DatabaseBackupOptions> options,
        ILogger<DatabaseBackupManager> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = CloneOptions(options.Value);
    }

    public DatabaseBackupOptions GetOptions()
    {
        EnsurePersistedOptionsLoaded();
        lock (_lock)
        {
            return CloneOptions(_options);
        }
    }

    public DatabaseBackupOverviewDto GetOverview()
    {
        EnsurePersistedOptionsLoaded();
        lock (_lock)
        {
            return new DatabaseBackupOverviewDto
            {
                Options = ToOptionsDto(_options),
                Status = CloneStatus(_status),
                Files = ListBackupFiles(_options.BackupDirectory)
            };
        }
    }

    public DatabaseBackupOverviewDto UpdateOptions(UpdateDatabaseBackupOptionsRequest request)
    {
        EnsurePersistedOptionsLoaded();
        lock (_lock)
        {
            var options = NormalizeOptions(new DatabaseBackupOptions
            {
                Enabled = request.Enabled,
                RunAtLocalTime = string.IsNullOrWhiteSpace(request.RunAtLocalTime)
                    ? null
                    : request.RunAtLocalTime.Trim(),
                BackupDirectory = request.BackupDirectory,
                RetentionCount = request.RetentionCount
            });

            SavePersistedOptions(options);
            _options = CloneOptions(options);

            return new DatabaseBackupOverviewDto
            {
                Options = ToOptionsDto(_options),
                Status = CloneStatus(_status),
                Files = ListBackupFiles(_options.BackupDirectory)
            };
        }
    }

    public async Task<DatabaseBackupRunResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        EnsurePersistedOptionsLoaded();

        DatabaseBackupOptions options;
        DateTime startedAt;
        lock (_lock)
        {
            if (_status.IsRunning)
            {
                return new DatabaseBackupRunResult(false, false, "数据库备份正在执行，请稍后再试。", CloneStatus(_status));
            }

            options = CloneOptions(_options);
            startedAt = DateTime.UtcNow;
            _status = new DatabaseBackupStatusDto
            {
                IsRunning = true,
                LastStartedAt = startedAt
            };
            PersistStatus(_status);
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<IDatabaseBackupExecutor>();
            var result = await executor.BackupAsync(options, cancellationToken);
            _logger.LogInformation("数据库备份完成：{FileName}", result.FileName);
            return Finish(startedAt, succeeded: true, error: null, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Finish(startedAt, succeeded: false, error: "数据库备份已取消。", result: null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "数据库备份失败");
            return Finish(startedAt, succeeded: false, error: ex.Message, result: null);
        }
    }

    private DatabaseBackupRunResult Finish(
        DateTime startedAt,
        bool succeeded,
        string? error,
        DatabaseBackupExecutionResult? result)
    {
        lock (_lock)
        {
            _status = new DatabaseBackupStatusDto
            {
                IsRunning = false,
                LastStartedAt = startedAt,
                LastFinishedAt = DateTime.UtcNow,
                LastSucceeded = succeeded,
                LastError = error,
                LastFileName = result?.FileName,
                LastFileSizeBytes = result?.FileSizeBytes
            };
            PersistStatus(_status);
            return new DatabaseBackupRunResult(true, succeeded, error, CloneStatus(_status));
        }
    }

    private void EnsurePersistedOptionsLoaded()
    {
        if (_persistedOptionsLoaded)
            return;

        lock (_lock)
        {
            if (_persistedOptionsLoaded)
                return;

            var setting = LoadSetting();
            if (setting is not null)
            {
                _options = ToOptions(setting);
                _status = ToStatus(setting);
            }

            _persistedOptionsLoaded = true;
        }
    }

    private DatabaseBackupSetting? LoadSetting()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<AppDbContext>();
        return db?.DatabaseBackupSettings
            .OrderBy(item => item.Id)
            .FirstOrDefault();
    }

    private void SavePersistedOptions(DatabaseBackupOptions options)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<AppDbContext>();
        if (db is null)
            return;

        var setting = GetOrCreateSetting(db);
        setting.Enabled = options.Enabled;
        setting.RunAtLocalTime = options.RunAtLocalTime;
        setting.BackupDirectory = options.BackupDirectory;
        setting.RetentionCount = options.RetentionCount;
        setting.UpdatedAt = DateTime.UtcNow;
        db.SaveChanges();
    }

    private void PersistStatus(DatabaseBackupStatusDto status)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<AppDbContext>();
        if (db is null)
            return;

        var setting = GetOrCreateSetting(db);
        setting.LastStartedAt = status.LastStartedAt;
        setting.LastFinishedAt = status.LastFinishedAt;
        setting.LastSucceeded = status.LastSucceeded;
        setting.LastError = status.LastError;
        setting.LastFileName = status.LastFileName;
        setting.LastFileSizeBytes = status.LastFileSizeBytes;
        setting.UpdatedAt = DateTime.UtcNow;
        db.SaveChanges();
    }

    private DatabaseBackupSetting GetOrCreateSetting(AppDbContext db)
    {
        var setting = db.DatabaseBackupSettings
            .OrderBy(item => item.Id)
            .FirstOrDefault();
        if (setting is not null)
            return setting;

        setting = new DatabaseBackupSetting
        {
            CreatedAt = DateTime.UtcNow,
            Enabled = _options.Enabled,
            RunAtLocalTime = _options.RunAtLocalTime,
            BackupDirectory = _options.BackupDirectory,
            RetentionCount = _options.RetentionCount
        };
        db.DatabaseBackupSettings.Add(setting);
        return setting;
    }

    private static DatabaseBackupOptions NormalizeOptions(DatabaseBackupOptions options)
    {
        options.BackupDirectory = string.IsNullOrWhiteSpace(options.BackupDirectory)
            ? "/app/backups"
            : options.BackupDirectory.Trim();
        options.RetentionCount = Math.Clamp(options.RetentionCount, 1, 365);
        return options;
    }

    private static DatabaseBackupOptions CloneOptions(DatabaseBackupOptions options)
        => NormalizeOptions(new DatabaseBackupOptions
        {
            Enabled = options.Enabled,
            RunAtLocalTime = options.RunAtLocalTime,
            BackupDirectory = options.BackupDirectory,
            RetentionCount = options.RetentionCount
        });

    private static DatabaseBackupOptions ToOptions(DatabaseBackupSetting setting)
        => NormalizeOptions(new DatabaseBackupOptions
        {
            Enabled = setting.Enabled,
            RunAtLocalTime = setting.RunAtLocalTime,
            BackupDirectory = setting.BackupDirectory,
            RetentionCount = setting.RetentionCount
        });

    private static DatabaseBackupOptionsDto ToOptionsDto(DatabaseBackupOptions options)
        => new()
        {
            Enabled = options.Enabled,
            RunAtLocalTime = options.RunAtLocalTime,
            BackupDirectory = options.BackupDirectory,
            RetentionCount = options.RetentionCount
        };

    private static DatabaseBackupStatusDto ToStatus(DatabaseBackupSetting setting)
        => new()
        {
            IsRunning = false,
            LastStartedAt = setting.LastStartedAt,
            LastFinishedAt = setting.LastFinishedAt,
            LastSucceeded = setting.LastSucceeded,
            LastError = setting.LastError,
            LastFileName = setting.LastFileName,
            LastFileSizeBytes = setting.LastFileSizeBytes
        };

    private static DatabaseBackupStatusDto CloneStatus(DatabaseBackupStatusDto status)
        => new()
        {
            IsRunning = status.IsRunning,
            LastStartedAt = status.LastStartedAt,
            LastFinishedAt = status.LastFinishedAt,
            LastSucceeded = status.LastSucceeded,
            LastError = status.LastError,
            LastFileName = status.LastFileName,
            LastFileSizeBytes = status.LastFileSizeBytes
        };

    private static List<DatabaseBackupFileDto> ListBackupFiles(string backupDirectory)
    {
        try
        {
            if (!Directory.Exists(backupDirectory))
                return [];

            return new DirectoryInfo(backupDirectory)
                .EnumerateFiles("*.sql.gz")
                .OrderByDescending(file => file.CreationTimeUtc)
                .Take(20)
                .Select(file => new DatabaseBackupFileDto
                {
                    FileName = file.Name,
                    SizeBytes = file.Length,
                    CreatedAt = file.CreationTimeUtc
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}

public sealed record DatabaseBackupRunResult(
    bool Started,
    bool Succeeded,
    string? Error,
    DatabaseBackupStatusDto Status);
