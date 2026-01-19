using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 文本处理配置Repository接口
/// </summary>
public interface ITextProcessingConfigRepository
{
    /// <summary>
    /// 获取文本处理配置（单例）
    /// </summary>
    /// <returns>文本处理配置</returns>
    Task<TextProcessingConfig> GetConfigAsync();

    /// <summary>
    /// 保存文本处理配置
    /// </summary>
    /// <param name="config">配置对象</param>
    Task SaveConfigAsync(TextProcessingConfig config);

    /// <summary>
    /// 重置为默认配置
    /// </summary>
    /// <returns>重置后的配置</returns>
    Task<TextProcessingConfig> ResetToDefaultAsync();
}
