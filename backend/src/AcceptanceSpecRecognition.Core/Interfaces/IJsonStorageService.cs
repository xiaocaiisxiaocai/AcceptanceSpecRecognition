namespace AcceptanceSpecRecognition.Core.Interfaces;

/// <summary>
/// JSON存储服务接口
/// </summary>
public interface IJsonStorageService
{
    /// <summary>
    /// 读取JSON文件
    /// </summary>
    Task<T?> ReadAsync<T>(string path) where T : class;
    
    /// <summary>
    /// 写入JSON文件
    /// </summary>
    Task WriteAsync<T>(string path, T data) where T : class;
    
    /// <summary>
    /// 检查文件是否存在
    /// </summary>
    bool Exists(string path);
    
    /// <summary>
    /// 确保目录存在
    /// </summary>
    void EnsureDirectoryExists(string path);
}
