using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Core.Interfaces;

/// <summary>
/// 健康检查服务接口
/// </summary>
public interface IHealthCheckService
{
    /// <summary>
    /// 检查所有组件健康状态
    /// </summary>
    Task<HealthCheckResult> CheckAllAsync();

    /// <summary>
    /// 检查Embedding服务
    /// </summary>
    Task<ComponentHealth> CheckEmbeddingServiceAsync();

    /// <summary>
    /// 检查LLM服务
    /// </summary>
    Task<ComponentHealth> CheckLLMServiceAsync();

    /// <summary>
    /// 检查数据文件状态
    /// </summary>
    Task<ComponentHealth> CheckDataFilesAsync();

    /// <summary>
    /// 检查配置状态
    /// </summary>
    Task<ComponentHealth> CheckConfigAsync();
}
