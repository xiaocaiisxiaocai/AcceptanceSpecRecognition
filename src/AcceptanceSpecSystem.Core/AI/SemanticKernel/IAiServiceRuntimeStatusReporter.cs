using AcceptanceSpecSystem.Core.AI.Models;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

/// <summary>
/// 接收真实 AI 调用结果，用于修正短期运行可用性；实现不得持久化修改管理员启禁用配置。
/// </summary>
public interface IAiServiceRuntimeStatusReporter
{
    long CaptureGeneration(int serviceId) => 0;

    void ReportAvailable(int serviceId, AiServicePurpose purpose);

    void ReportUnavailable(int serviceId, AiServicePurpose purpose, string? message = null);

    void ReportAvailableIfCurrent(int serviceId, AiServicePurpose purpose, long expectedGeneration) =>
        ReportAvailable(serviceId, purpose);

    void ReportUnavailableIfCurrent(
        int serviceId,
        AiServicePurpose purpose,
        long expectedGeneration,
        string? message = null) => ReportUnavailable(serviceId, purpose, message);
}

public interface IAiServiceRuntimeAvailability
{
    long ConfigurationVersion => 0;

    bool IsAvailable(int serviceId, AiServicePurpose purpose);

    /// <summary>
    /// 显式指定服务时是否仍可尝试真实业务调用。
    /// Unknown/Checking 由受超时保护的业务调用确认，只有已知 Unavailable 才阻断。
    /// </summary>
    bool CanAttempt(int serviceId, AiServicePurpose purpose) =>
        IsAvailable(serviceId, purpose);
}

public sealed class NullAiServiceRuntimeStatusReporter : IAiServiceRuntimeStatusReporter
{
    public static NullAiServiceRuntimeStatusReporter Instance { get; } = new();

    private NullAiServiceRuntimeStatusReporter()
    {
    }

    public void ReportAvailable(int serviceId, AiServicePurpose purpose)
    {
    }

    public void ReportUnavailable(int serviceId, AiServicePurpose purpose, string? message = null)
    {
    }
}
