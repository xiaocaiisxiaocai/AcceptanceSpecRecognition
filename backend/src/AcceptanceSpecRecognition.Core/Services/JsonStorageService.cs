using System.Text.Json;
using AcceptanceSpecRecognition.Core.Interfaces;

namespace AcceptanceSpecRecognition.Core.Services;

/// <summary>
/// JSON存储服务实现 - 带并发控制
/// </summary>
public class JsonStorageService : IJsonStorageService
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // 使用ConcurrentDictionary为每个文件维护一个锁对象
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();

    private SemaphoreSlim GetFileLock(string path)
    {
        var normalizedPath = Path.GetFullPath(path).ToLowerInvariant();
        return _fileLocks.GetOrAdd(normalizedPath, _ => new SemaphoreSlim(1, 1));
    }

    public async Task<T?> ReadAsync<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var fileLock = GetFileLock(path);
        await fileLock.WaitAsync();
        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<T>(json, _options);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task WriteAsync<T>(string path, T data) where T : class
    {
        EnsureDirectoryExists(path);

        var fileLock = GetFileLock(path);
        await fileLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(data, _options);
            await File.WriteAllTextAsync(path, json);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public bool Exists(string path)
    {
        return File.Exists(path);
    }

    public void EnsureDirectoryExists(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
