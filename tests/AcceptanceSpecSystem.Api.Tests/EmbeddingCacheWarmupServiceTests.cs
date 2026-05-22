using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Api.Services;
using FluentAssertions;
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
            new CollectingLogger<EmbeddingCacheWarmupService>());
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
