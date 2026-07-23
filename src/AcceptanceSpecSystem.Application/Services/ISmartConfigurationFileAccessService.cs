using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 为智能结构配置提供当前请求用户可访问的上传文件。
/// 具体的数据范围解析由入口层实现，Application 不依赖 HTTP 或 API 授权类型。
/// </summary>
public interface ISmartConfigurationFileAccessService
{
    Task<WordFile?> GetAccessibleFileAsync(
        int fileId,
        CancellationToken cancellationToken = default);

    Task<bool> CanAccessCustomerAsync(
        int customerId,
        CancellationToken cancellationToken = default);
}
