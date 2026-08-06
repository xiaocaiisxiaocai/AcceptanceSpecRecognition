using AcceptanceSpecSystem.Application.Options;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.Extensions.Options;
using DataAiServicePurpose = AcceptanceSpecSystem.Data.Entities.AiServicePurpose;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 主机启动后的 AI 软预热。仅触发既有 readiness 调度，不阻断 HTTP 启动，
/// 也不绕过探测器的 single-flight、队列容量和并发上限。
/// </summary>
public sealed class AiServiceStartupPreloadService(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    IOptions<AiServiceReadinessOptions> options,
    ILogger<AiServiceStartupPreloadService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        if (!options.Value.PreloadOnStartup || environment.IsEnvironment("Testing"))
            return;

        foreach (var purpose in new[] { DataAiServicePurpose.Llm, DataAiServicePurpose.Embedding })
        {
            if (stoppingToken.IsCancellationRequested)
                return;

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var selection = scope.ServiceProvider.GetRequiredService<IAiServiceSelectionAppService>();
                var result = await selection.PreloadPreferredAsync(purpose, stoppingToken);
                logger.LogInformation(
                    "AI 启动预热已触发: purpose={Purpose}, serviceId={ServiceId}, status={Status}",
                    purpose,
                    result.ServiceId,
                    result.Status);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    "AI 启动预热触发失败: purpose={Purpose}, exceptionType={ExceptionType}",
                    purpose,
                    ex.GetType().Name);
            }
        }
    }
}
