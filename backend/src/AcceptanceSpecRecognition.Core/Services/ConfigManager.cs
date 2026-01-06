using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecRecognition.Core.Services;

/// <summary>
/// 配置管理器实现 - 使用异步初始化模式避免死锁
/// </summary>
public class ConfigManager : IConfigManager
{
    private readonly IJsonStorageService _storage;
    private readonly ILogger<ConfigManager> _logger;
    private readonly string _configPath;
    private SystemConfig _config;
    private readonly List<ConfigChange> _history = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// 私有构造函数，使用工厂方法创建实例
    /// </summary>
    private ConfigManager(IJsonStorageService storage, ILogger<ConfigManager> logger)
    {
        _storage = storage;
        _logger = logger;
        _configPath = "./data/config.json";
        _config = GetDefaultConfig(); // 先使用默认配置
        _initialized = false;
    }

    /// <summary>
    /// 异步工厂方法 - 创建并初始化 ConfigManager
    /// </summary>
    public static async Task<ConfigManager> CreateAsync(IJsonStorageService storage, ILogger<ConfigManager> logger)
    {
        var manager = new ConfigManager(storage, logger);
        await manager.InitializeAsync();
        return manager;
    }

    /// <summary>
    /// 同步工厂方法 - 用于 DI 注册，使用默认配置后台加载
    /// </summary>
    public static ConfigManager Create(IJsonStorageService storage, ILogger<ConfigManager> logger)
    {
        var manager = new ConfigManager(storage, logger);
        // 启动后台初始化，不阻塞构造
        _ = manager.InitializeAsync();
        return manager;
    }

    /// <summary>
    /// 异步初始化配置
    /// </summary>
    private async Task InitializeAsync()
    {
        try
        {
            var config = await _storage.ReadAsync<SystemConfig>(_configPath);
            if (config != null)
            {
                _config = config;
            }
            _initialized = true;
            _logger.LogInformation("配置管理器初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载配置文件失败，使用默认配置");
            _initialized = true;
        }
    }

    /// <summary>
    /// 确保配置已加载（公开方法，用于启动时初始化）
    /// </summary>
    public async Task EnsureInitializedAsync()
    {
        if (!_initialized)
        {
            await InitializeAsync();
        }
    }

    private SystemConfig GetDefaultConfig()
    {
        return new SystemConfig
        {
            Version = "1.0",
            Embedding = new EmbeddingConfig(),
            LLM = new LLMConfig(),
            Matching = new MatchingConfig(),
            Highlighting = new HighlightingConfig()
        };
    }

    public T Get<T>(string key)
    {
        var property = typeof(SystemConfig).GetProperty(key);
        if (property == null)
        {
            throw new ArgumentException($"Configuration key '{key}' not found");
        }
        return (T)property.GetValue(_config)!;
    }

    /// <summary>
    /// 同步设置配置 - 使用 Fire-and-Forget 模式保存
    /// </summary>
    public void Set<T>(string key, T value)
    {
        var property = typeof(SystemConfig).GetProperty(key);
        if (property == null)
        {
            throw new ArgumentException($"Configuration key '{key}' not found");
        }

        var oldValue = property.GetValue(_config);
        property.SetValue(_config, value);

        _history.Add(new ConfigChange
        {
            Key = key,
            OldValue = oldValue,
            NewValue = value,
            ChangedBy = "system",
            ChangedAt = DateTime.UtcNow
        });

        // Fire-and-Forget 保存，不阻塞当前线程
        _ = SaveConfigAsync();
    }

    /// <summary>
    /// 异步设置配置
    /// </summary>
    public async Task SetAsync<T>(string key, T value)
    {
        var property = typeof(SystemConfig).GetProperty(key);
        if (property == null)
        {
            throw new ArgumentException($"Configuration key '{key}' not found");
        }

        var oldValue = property.GetValue(_config);
        property.SetValue(_config, value);

        _history.Add(new ConfigChange
        {
            Key = key,
            OldValue = oldValue,
            NewValue = value,
            ChangedBy = "system",
            ChangedAt = DateTime.UtcNow
        });

        await SaveConfigAsync();
    }

    public SystemConfig GetAll()
    {
        return _config;
    }

    /// <summary>
    /// 同步更新配置 - 使用 Fire-and-Forget 模式保存
    /// </summary>
    public void UpdateConfig(SystemConfig config)
    {
        _config = config;
        // Fire-and-Forget 保存
        _ = SaveConfigAsync();
    }

    /// <summary>
    /// 异步更新配置
    /// </summary>
    public async Task UpdateConfigAsync(SystemConfig config)
    {
        _config = config;
        await SaveConfigAsync();
    }

    public async Task UpdateMatchingConfigAsync(MatchingConfig config)
    {
        var oldValue = _config.Matching;
        _config.Matching = config;

        _history.Add(new ConfigChange
        {
            Key = "Matching",
            OldValue = oldValue,
            NewValue = config,
            ChangedBy = "user",
            ChangedAt = DateTime.UtcNow
        });

        await SaveConfigAsync();
    }

    public async Task UpdatePreprocessingConfigAsync(PreprocessingConfig config)
    {
        _history.Add(new ConfigChange
        {
            Key = "Preprocessing",
            OldValue = null,
            NewValue = config,
            ChangedBy = "user",
            ChangedAt = DateTime.UtcNow
        });

        await SaveConfigAsync();
    }

    /// <summary>
    /// 异步重载配置
    /// </summary>
    public async Task ReloadAsync()
    {
        await EnsureInitializedAsync();
        var config = await _storage.ReadAsync<SystemConfig>(_configPath);
        if (config != null)
        {
            _config = config;
        }
        _logger.LogInformation("配置已重新加载");
    }

    public List<ConfigChange> GetHistory()
    {
        return _history.ToList();
    }

    /// <summary>
    /// 带锁的异步保存配置
    /// </summary>
    private async Task SaveConfigAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            await _storage.WriteAsync(_configPath, _config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存配置失败");
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
