using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Tests;

public class DatabaseBackupServiceTests
{
    [Fact]
    public void GetOverview_WhenDatabaseContainsOverride_ShouldUsePersistedOptions()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var services = CreateServices(connection, new DatabaseBackupOptions
        {
            Enabled = false,
            RunAtLocalTime = "02:00",
            BackupDirectory = "/app/backups",
            RetentionCount = 7
        });

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            db.DatabaseBackupSettings.Add(new DatabaseBackupSetting
            {
                Enabled = true,
                RunAtLocalTime = "03:30",
                BackupDirectory = "/data/db-backups",
                RetentionCount = 14,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var manager = CreateManager(services);

        var overview = manager.GetOverview();

        overview.Options.Enabled.Should().BeTrue();
        overview.Options.RunAtLocalTime.Should().Be("03:30");
        overview.Options.BackupDirectory.Should().Be("/data/db-backups");
        overview.Options.RetentionCount.Should().Be(14);
    }

    [Fact]
    public void UpdateOptions_ShouldPersistOverrideToDatabase()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var services = CreateServices(connection, new DatabaseBackupOptions());

        using (var scope = services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        }

        var manager = CreateManager(services);

        manager.UpdateOptions(new UpdateDatabaseBackupOptionsRequest
        {
            Enabled = true,
            RunAtLocalTime = "04:45",
            BackupDirectory = "/backup/mysql",
            RetentionCount = 10
        });

        using var verifyScope = services.CreateScope();
        var setting = verifyScope.ServiceProvider
            .GetRequiredService<AppDbContext>()
            .DatabaseBackupSettings
            .Single();

        setting.Enabled.Should().BeTrue();
        setting.RunAtLocalTime.Should().Be("04:45");
        setting.BackupDirectory.Should().Be("/backup/mysql");
        setting.RetentionCount.Should().Be(10);
        setting.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunOnceAsync_ShouldExecuteBackupAndPersistLastResult()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var executor = new RecordingDatabaseBackupExecutor
        {
            Result = new DatabaseBackupExecutionResult("acceptance-20260522020000.sql.gz", 128)
        };
        var services = CreateServices(connection, new DatabaseBackupOptions(), executor);

        using (var scope = services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        }

        var manager = CreateManager(services);
        manager.UpdateOptions(new UpdateDatabaseBackupOptionsRequest
        {
            Enabled = true,
            RunAtLocalTime = "02:00",
            BackupDirectory = "/backup/mysql",
            RetentionCount = 7
        });

        var result = await manager.RunOnceAsync(CancellationToken.None);

        result.Started.Should().BeTrue();
        result.Succeeded.Should().BeTrue();
        executor.Calls.Should().Be(1);
        executor.Options.BackupDirectory.Should().Be("/backup/mysql");
        executor.Options.RetentionCount.Should().Be(7);

        using var verifyScope = services.CreateScope();
        var setting = verifyScope.ServiceProvider
            .GetRequiredService<AppDbContext>()
            .DatabaseBackupSettings
            .Single();

        setting.LastSucceeded.Should().BeTrue();
        setting.LastFileName.Should().Be("acceptance-20260522020000.sql.gz");
        setting.LastError.Should().BeNull();
        setting.LastFinishedAt.Should().NotBeNull();
    }

    private static ServiceProvider CreateServices(
        SqliteConnection connection,
        DatabaseBackupOptions options,
        IDatabaseBackupExecutor? executor = null)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<DatabaseBackupOptions>(current =>
        {
            current.Enabled = options.Enabled;
            current.RunAtLocalTime = options.RunAtLocalTime;
            current.BackupDirectory = options.BackupDirectory;
            current.RetentionCount = options.RetentionCount;
        });
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=mysql;Port=3306;Database=acceptance;User=acceptance;Password=acceptance123;CharSet=utf8mb4;"
            })
            .Build());
        services.AddDbContext<AppDbContext>(builder => builder.UseSqlite(connection));
        services.AddSingleton(executor ?? new RecordingDatabaseBackupExecutor());

        return services.BuildServiceProvider();
    }

    private static DatabaseBackupManager CreateManager(ServiceProvider services)
        => new(
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IOptions<DatabaseBackupOptions>>(),
            NullLogger<DatabaseBackupManager>.Instance);

    private sealed class RecordingDatabaseBackupExecutor : IDatabaseBackupExecutor
    {
        public int Calls { get; private set; }
        public DatabaseBackupOptions Options { get; private set; } = new();
        public DatabaseBackupExecutionResult Result { get; set; } = new("backup.sql.gz", 1);

        public Task<DatabaseBackupExecutionResult> BackupAsync(
            DatabaseBackupOptions options,
            CancellationToken cancellationToken)
        {
            Calls++;
            Options = options;
            return Task.FromResult(Result);
        }
    }
}
