using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Core.Interfaces;

/// <summary>
/// 配置管理器接口
/// </summary>
public interface IConfigManager
{
    /// <summary>
    /// 确保配置已初始化（异步）
    /// </summary>
    Task EnsureInitializedAsync();

    /// <summary>
    /// 获取配置
    /// </summary>
    T Get<T>(string key);

    /// <summary>
    /// 设置配置（同步版本，内部使用Fire-and-Forget保存）
    /// </summary>
    void Set<T>(string key, T value);

    /// <summary>
    /// 设置配置（异步版本，推荐使用）
    /// </summary>
    Task SetAsync<T>(string key, T value);

    /// <summary>
    /// 获取所有配置
    /// </summary>
    SystemConfig GetAll();

    /// <summary>
    /// 更新配置（同步版本，内部使用Fire-and-Forget保存）
    /// </summary>
    void UpdateConfig(SystemConfig config);

    /// <summary>
    /// 更新配置（异步版本，推荐使用）
    /// </summary>
    Task UpdateConfigAsync(SystemConfig config);

    /// <summary>
    /// 更新匹配配置
    /// </summary>
    Task UpdateMatchingConfigAsync(MatchingConfig config);

    /// <summary>
    /// 更新预处理配置
    /// </summary>
    Task UpdatePreprocessingConfigAsync(PreprocessingConfig config);

    /// <summary>
    /// 重载配置文件（异步版本）
    /// </summary>
    Task ReloadAsync();

    /// <summary>
    /// 记录配置修改历史
    /// </summary>
    List<ConfigChange> GetHistory();
}
