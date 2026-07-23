using AcceptanceSpecSystem.Application.Options;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Tests;

public class EmbeddingCacheWarmupServiceTests
{
    [Fact]
    public async Task StartAsync_WhenDisabled_ShouldNotExecuteWarmup()
    {
        var executor = new RecordingWarmupExecutor();
        var service = CreateService(new EmbeddingCacheWarmupOptions
        {
            Enabled = false,
            RunOnStartup = true,
            IntervalHours = 1
        }, executor, out _);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await service.StartAsync(cts.Token);
        await Task.Delay(50, CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        executor.Calls.Should().Be(0);
    }

    [Fact]
    public async Task StartAsync_WhenRunOnStartup_ShouldExecuteWarmupOnce()
    {
        var executor = new RecordingWarmupExecutor();
        var service = CreateService(new EmbeddingCacheWarmupOptions
        {
            Enabled = true,
            RunOnStartup = true,
            IntervalHours = 24,
            BatchSize = 25,
            MaxItemsPerRun = 200
        }, executor, out _);

        await service.StartAsync(CancellationToken.None);

        (await executor.WaitForCallsAsync(1, TimeSpan.FromSeconds(2))).Should().BeTrue();
        await service.StopAsync(CancellationToken.None);

        executor.Calls.Should().Be(1);
        executor.BatchSize.Should().Be(25);
        executor.MaxItemsPerRun.Should().Be(200);
    }

    [Fact]
    public async Task StartAsync_WhenWarmupThrows_ShouldLogAndNotThrow()
    {
        var executor = new RecordingWarmupExecutor
        {
            ExceptionToThrow = new InvalidOperationException("预热失败")
        };
        var service = CreateService(new EmbeddingCacheWarmupOptions
        {
            Enabled = true,
            RunOnStartup = true,
            IntervalHours = 24
        }, executor, out var logger);

        var action = async () =>
        {
            await service.StartAsync(CancellationToken.None);
            (await executor.WaitForCallsAsync(1, TimeSpan.FromSeconds(2))).Should().BeTrue();
            await service.StopAsync(CancellationToken.None);
        };

        await action.Should().NotThrowAsync();
        logger.WarningMessages.Should().Contain(message => message.Contains("向量缓存预热失败", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Trigger_WhenSeveralRequestsArePending_ShouldMergeIntoOneWarmup()
    {
        var executor = new RecordingWarmupExecutor();
        using var trigger = new EmbeddingCacheWarmupTrigger();
        var service = CreateService(new EmbeddingCacheWarmupOptions
        {
            Enabled = false,
            IntervalHours = 24
        }, executor, trigger, out _);

        trigger.Request().Should().BeTrue();
        trigger.Request().Should().BeFalse();
        await service.StartAsync(CancellationToken.None);

        (await executor.WaitForCallsAsync(1, TimeSpan.FromSeconds(2))).Should().BeTrue();
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        executor.Calls.Should().Be(1);
    }

    [Fact]
    public async Task StopAsync_WhenTriggeredWarmupIsRunning_ShouldCancelWithHostToken()
    {
        var executor = new CancellationAwareWarmupExecutor();
        using var trigger = new EmbeddingCacheWarmupTrigger();
        var service = CreateService(new EmbeddingCacheWarmupOptions
        {
            Enabled = false,
            IntervalHours = 24
        }, executor, trigger, out _);

        await service.StartAsync(CancellationToken.None);
        trigger.Request().Should().BeTrue();
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await service.StopAsync(CancellationToken.None);

        (await executor.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();
    }

    [Fact]
    public async Task Trigger_WaitShouldObserveCancellation()
    {
        using var trigger = new EmbeddingCacheWarmupTrigger();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var action = async () => await trigger.WaitAsync(cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void GetOptions_WhenDatabaseContainsOverride_ShouldUsePersistedOptions()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = CreateServicesWithDatabase(
            connection,
            new EmbeddingCacheWarmupOptions
            {
                Enabled = false,
                RunOnStartup = false,
                IntervalHours = 24,
                BatchSize = 100,
                MaxItemsPerRun = 1000
            });

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            db.EmbeddingCacheWarmupSettings.Add(new EmbeddingCacheWarmupSetting
            {
                Enabled = true,
                RunOnStartup = true,
                RunAtLocalTime = "03:30",
                IntervalHours = 6,
                BatchSize = 25,
                MaxItemsPerRun = 250,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var manager = new EmbeddingCacheWarmupManager(
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IOptions<EmbeddingCacheWarmupOptions>>(),
            new CollectingLogger<EmbeddingCacheWarmupManager>());

        var options = manager.GetOptions();

        options.Enabled.Should().BeTrue();
        options.RunOnStartup.Should().BeTrue();
        options.RunAtLocalTime.Should().Be("03:30");
        options.IntervalHours.Should().Be(6);
        options.BatchSize.Should().Be(25);
        options.MaxItemsPerRun.Should().Be(250);
    }

    [Fact]
    public void UpdateOptions_ShouldPersistOverrideToDatabase()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = CreateServicesWithDatabase(
            connection,
            new EmbeddingCacheWarmupOptions
            {
                Enabled = false,
                RunOnStartup = false,
                IntervalHours = 24,
                BatchSize = 100,
                MaxItemsPerRun = 1000
            });

        using (var scope = services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        }

        var manager = new EmbeddingCacheWarmupManager(
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IOptions<EmbeddingCacheWarmupOptions>>(),
            new CollectingLogger<EmbeddingCacheWarmupManager>());

        manager.UpdateOptions(new()
        {
            Enabled = true,
            RunOnStartup = true,
            RunAtLocalTime = "04:45",
            IntervalHours = 8,
            BatchSize = 32,
            MaxItemsPerRun = 320
        });

        using var verifyScope = services.CreateScope();
        var setting = verifyScope.ServiceProvider
            .GetRequiredService<AppDbContext>()
            .EmbeddingCacheWarmupSettings
            .Single();

        setting.Enabled.Should().BeTrue();
        setting.RunOnStartup.Should().BeTrue();
        setting.RunAtLocalTime.Should().Be("04:45");
        setting.IntervalHours.Should().Be(8);
        setting.BatchSize.Should().Be(32);
        setting.MaxItemsPerRun.Should().Be(320);
        setting.UpdatedAt.Should().NotBeNull();
    }

    private static EmbeddingCacheWarmupService CreateService(
        EmbeddingCacheWarmupOptions options,
        IEmbeddingCacheWarmupExecutor executor,
        out CollectingLogger<EmbeddingCacheWarmupManager> logger)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<EmbeddingCacheWarmupOptions>(current =>
        {
            current.Enabled = options.Enabled;
            current.RunOnStartup = options.RunOnStartup;
            current.RunAtLocalTime = options.RunAtLocalTime;
            current.IntervalHours = options.IntervalHours;
            current.BatchSize = options.BatchSize;
            current.MaxItemsPerRun = options.MaxItemsPerRun;
        });
        services.AddSingleton(executor);

        var provider = services.BuildServiceProvider();
        logger = new CollectingLogger<EmbeddingCacheWarmupManager>();
        var manager = new EmbeddingCacheWarmupManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<EmbeddingCacheWarmupOptions>>(),
            logger);

        return new EmbeddingCacheWarmupService(
            manager,
            new EmbeddingCacheWarmupTrigger(),
            new CollectingLogger<EmbeddingCacheWarmupService>());
    }

    private static EmbeddingCacheWarmupService CreateService(
        EmbeddingCacheWarmupOptions options,
        IEmbeddingCacheWarmupExecutor executor,
        IEmbeddingCacheWarmupTrigger trigger,
        out CollectingLogger<EmbeddingCacheWarmupManager> logger)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<EmbeddingCacheWarmupOptions>(current =>
        {
            current.Enabled = options.Enabled;
            current.RunOnStartup = options.RunOnStartup;
            current.RunAtLocalTime = options.RunAtLocalTime;
            current.IntervalHours = options.IntervalHours;
            current.BatchSize = options.BatchSize;
            current.MaxItemsPerRun = options.MaxItemsPerRun;
        });
        services.AddSingleton(executor);

        var provider = services.BuildServiceProvider();
        logger = new CollectingLogger<EmbeddingCacheWarmupManager>();
        var manager = new EmbeddingCacheWarmupManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<EmbeddingCacheWarmupOptions>>(),
            logger);
        return new EmbeddingCacheWarmupService(
            manager,
            trigger,
            new CollectingLogger<EmbeddingCacheWarmupService>());
    }

    private static ServiceProvider CreateServicesWithDatabase(
        SqliteConnection connection,
        EmbeddingCacheWarmupOptions options)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<EmbeddingCacheWarmupOptions>(current =>
        {
            current.Enabled = options.Enabled;
            current.RunOnStartup = options.RunOnStartup;
            current.RunAtLocalTime = options.RunAtLocalTime;
            current.IntervalHours = options.IntervalHours;
            current.BatchSize = options.BatchSize;
            current.MaxItemsPerRun = options.MaxItemsPerRun;
        });
        services.AddDbContext<AppDbContext>(builder => builder.UseSqlite(connection));

        return services.BuildServiceProvider();
    }

    private sealed class RecordingWarmupExecutor : IEmbeddingCacheWarmupExecutor
    {
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }
        public int BatchSize { get; private set; }
        public int MaxItemsPerRun { get; private set; }
        public Exception? ExceptionToThrow { get; set; }

        public Task WarmupAsync(int batchSize, int maxItemsPerRun, CancellationToken cancellationToken)
        {
            Calls++;
            BatchSize = batchSize;
            MaxItemsPerRun = maxItemsPerRun;
            _called.TrySetResult();

            return ExceptionToThrow is null
                ? Task.CompletedTask
                : Task.FromException(ExceptionToThrow);
        }

        public async Task<bool> WaitForCallsAsync(int expectedCalls, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);

            while (!cts.IsCancellationRequested)
            {
                if (Calls >= expectedCalls)
                    return true;

                try
                {
                    await Task.WhenAny(_called.Task, Task.Delay(20, cts.Token));
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            return Calls >= expectedCalls;
        }
    }

    private sealed class CancellationAwareWarmupExecutor : IEmbeddingCacheWarmupExecutor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WarmupAsync(int batchSize, int maxItemsPerRun, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved.TrySetResult(true);
                throw;
            }
        }
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<string> WarningMessages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningMessages.Add(formatter(state, exception));
            }
        }
    }
}
