using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class EmbeddingCacheWarmupManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmbeddingCacheWarmupManager> _logger;
    private readonly object _lock = new();
    private EmbeddingCacheWarmupOptions _options;
    private EmbeddingCacheWarmupStatusDto _status = new();
    private bool _persistedOptionsLoaded;

    public EmbeddingCacheWarmupManager(
        IServiceScopeFactory scopeFactory,
        IOptions<EmbeddingCacheWarmupOptions> options,
        ILogger<EmbeddingCacheWarmupManager> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = CloneOptions(options.Value);
    }

    public EmbeddingCacheWarmupOptions GetOptions()
    {
        EnsurePersistedOptionsLoaded();

        lock (_lock)
        {
            return CloneOptions(_options);
        }
    }

    public EmbeddingCacheWarmupOverviewDto GetOverview()
    {
        EnsurePersistedOptionsLoaded();

        lock (_lock)
        {
            return new EmbeddingCacheWarmupOverviewDto
            {
                Options = ToOptionsDto(_options),
                Status = CloneStatus(_status),
                LastResult = ToLastResult(_status)
            };
        }
    }

    public EmbeddingCacheWarmupOverviewDto UpdateOptions(UpdateEmbeddingCacheWarmupOptionsRequest request)
    {
        EnsurePersistedOptionsLoaded();

        lock (_lock)
        {
            var options = NormalizeOptions(new EmbeddingCacheWarmupOptions
            {
                Enabled = request.Enabled,
                RunOnStartup = request.RunOnStartup,
                RunAtLocalTime = string.IsNullOrWhiteSpace(request.RunAtLocalTime)
                    ? null
                    : request.RunAtLocalTime.Trim(),
                IntervalHours = request.IntervalHours,
                BatchSize = request.BatchSize,
                MaxItemsPerRun = request.MaxItemsPerRun
            });

            SavePersistedOptions(options);
            _options = CloneOptions(options);

            return new EmbeddingCacheWarmupOverviewDto
            {
                Options = ToOptionsDto(_options),
                Status = CloneStatus(_status),
                LastResult = ToLastResult(_status)
            };
        }
    }

    /// <summary>
    /// 触发一次向量缓存预热；同一进程内只允许一个任务运行，执行器未注册时视为跳过成功。
    /// </summary>
    public async Task<EmbeddingCacheWarmupRunResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        EnsurePersistedOptionsLoaded();

        EmbeddingCacheWarmupOptions options;
        DateTime startedAt;
        int batchSize;
        int maxItemsPerRun;

        lock (_lock)
        {
            if (_status.IsRunning)
            {
                return new EmbeddingCacheWarmupRunResult(false, false, "向量缓存预热正在执行，请稍后再试。", CloneStatus(_status));
            }

            options = CloneOptions(_options);
            batchSize = Math.Max(1, options.BatchSize);
            maxItemsPerRun = Math.Max(1, options.MaxItemsPerRun);
            startedAt = DateTime.UtcNow;
            _status = new EmbeddingCacheWarmupStatusDto
            {
                IsRunning = true,
                LastStartedAt = startedAt,
                LastBatchSize = batchSize,
                LastMaxItemsPerRun = maxItemsPerRun
            };
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var executor = scope.ServiceProvider.GetService<IEmbeddingCacheWarmupExecutor>();
            if (executor is null)
            {
                _logger.LogDebug("向量缓存预热执行器未注册，跳过本次预热");
                return Finish(startedAt, succeeded: true, error: null);
            }

            await executor.WarmupAsync(batchSize, maxItemsPerRun, cancellationToken);
            _logger.LogInformation("向量缓存预热完成：BatchSize={BatchSize}, MaxItemsPerRun={MaxItemsPerRun}", batchSize, maxItemsPerRun);
            return Finish(startedAt, succeeded: true, error: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Finish(startedAt, succeeded: false, error: "向量缓存预热已取消。");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "向量缓存预热失败");
            return Finish(startedAt, succeeded: false, error: ex.Message);
        }
    }

    private EmbeddingCacheWarmupRunResult Finish(DateTime startedAt, bool succeeded, string? error)
    {
        lock (_lock)
        {
            _status = new EmbeddingCacheWarmupStatusDto
            {
                IsRunning = false,
                LastStartedAt = startedAt,
                LastFinishedAt = DateTime.UtcNow,
                LastSucceeded = succeeded,
                LastError = error,
                LastBatchSize = _status.LastBatchSize,
                LastMaxItemsPerRun = _status.LastMaxItemsPerRun
            };

            return new EmbeddingCacheWarmupRunResult(true, succeeded, error, CloneStatus(_status));
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

            var persistedOptions = LoadPersistedOptions();
            if (persistedOptions is not null)
            {
                // 数据库覆盖值用于承接管理页保存结果；appsettings 仍作为首次默认值。
                _options = persistedOptions;
            }

            _persistedOptionsLoaded = true;
        }
    }

    private EmbeddingCacheWarmupOptions? LoadPersistedOptions()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<AppDbContext>();
        var setting = db?.EmbeddingCacheWarmupSettings
            .OrderBy(item => item.Id)
            .FirstOrDefault();

        return setting is null ? null : ToOptions(setting);
    }

    private void SavePersistedOptions(EmbeddingCacheWarmupOptions options)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<AppDbContext>();
        if (db is null)
            return;

        var now = DateTime.UtcNow;
        var setting = db.EmbeddingCacheWarmupSettings
            .OrderBy(item => item.Id)
            .FirstOrDefault();

        if (setting is null)
        {
            setting = new EmbeddingCacheWarmupSetting
            {
                CreatedAt = now
            };
            db.EmbeddingCacheWarmupSettings.Add(setting);
        }

        setting.Enabled = options.Enabled;
        setting.RunOnStartup = options.RunOnStartup;
        setting.RunAtLocalTime = options.RunAtLocalTime;
        setting.IntervalHours = options.IntervalHours;
        setting.BatchSize = options.BatchSize;
        setting.MaxItemsPerRun = options.MaxItemsPerRun;
        setting.UpdatedAt = now;

        db.SaveChanges();
    }

    private static EmbeddingCacheWarmupOptions NormalizeOptions(EmbeddingCacheWarmupOptions options)
    {
        options.IntervalHours = Math.Max(1, options.IntervalHours);
        options.BatchSize = Math.Max(1, options.BatchSize);
        options.MaxItemsPerRun = Math.Max(1, options.MaxItemsPerRun);
        return options;
    }

    private static EmbeddingCacheWarmupOptions CloneOptions(EmbeddingCacheWarmupOptions options)
        => NormalizeOptions(new EmbeddingCacheWarmupOptions
        {
            Enabled = options.Enabled,
            RunOnStartup = options.RunOnStartup,
            RunAtLocalTime = options.RunAtLocalTime,
            IntervalHours = options.IntervalHours,
            BatchSize = options.BatchSize,
            MaxItemsPerRun = options.MaxItemsPerRun
        });

    private static EmbeddingCacheWarmupOptions ToOptions(EmbeddingCacheWarmupSetting setting)
        => NormalizeOptions(new EmbeddingCacheWarmupOptions
        {
            Enabled = setting.Enabled,
            RunOnStartup = setting.RunOnStartup,
            RunAtLocalTime = setting.RunAtLocalTime,
            IntervalHours = setting.IntervalHours,
            BatchSize = setting.BatchSize,
            MaxItemsPerRun = setting.MaxItemsPerRun
        });

    private static EmbeddingCacheWarmupOptionsDto ToOptionsDto(EmbeddingCacheWarmupOptions options)
        => new()
        {
            Enabled = options.Enabled,
            RunOnStartup = options.RunOnStartup,
            RunAtLocalTime = options.RunAtLocalTime,
            IntervalHours = options.IntervalHours,
            BatchSize = options.BatchSize,
            MaxItemsPerRun = options.MaxItemsPerRun
        };

    private static EmbeddingCacheWarmupStatusDto CloneStatus(EmbeddingCacheWarmupStatusDto status)
        => new()
        {
            IsRunning = status.IsRunning,
            LastStartedAt = status.LastStartedAt,
            LastFinishedAt = status.LastFinishedAt,
            LastSucceeded = status.LastSucceeded,
            LastError = status.LastError,
            LastBatchSize = status.LastBatchSize,
            LastMaxItemsPerRun = status.LastMaxItemsPerRun
        };

    private static EmbeddingCacheWarmupLastResultDto? ToLastResult(EmbeddingCacheWarmupStatusDto status)
    {
        if (!status.LastStartedAt.HasValue || !status.LastFinishedAt.HasValue || !status.LastSucceeded.HasValue)
            return null;

        return new EmbeddingCacheWarmupLastResultDto
        {
            StartedAt = status.LastStartedAt.Value,
            FinishedAt = status.LastFinishedAt.Value,
            Succeeded = status.LastSucceeded.Value,
            Error = status.LastError,
            BatchSize = status.LastBatchSize ?? 0,
            MaxItemsPerRun = status.LastMaxItemsPerRun ?? 0
        };
    }
}

public sealed record EmbeddingCacheWarmupRunResult(
    bool Started,
    bool Succeeded,
    string? Error,
    EmbeddingCacheWarmupStatusDto Status);
