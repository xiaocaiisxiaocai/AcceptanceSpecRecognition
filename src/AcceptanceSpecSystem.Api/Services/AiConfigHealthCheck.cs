using AcceptanceSpecSystem.Application.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using CoreAiServicePurpose = AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class AiConfigHealthCheck : IHealthCheck
{
    private readonly AiServiceReadinessRegistry _readinessRegistry;

    public AiConfigHealthCheck(AiServiceReadinessRegistry readinessRegistry)
    {
        _readinessRegistry = readinessRegistry;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // 健康请求只读取短期缓存，不在此路径同步调用任何外部 AI 端点。
        var snapshots = _readinessRegistry.GetCurrentSnapshots();
        var llm = Aggregate(snapshots.Where(snapshot => snapshot.Purpose == CoreAiServicePurpose.Llm));
        var embedding = Aggregate(snapshots.Where(snapshot => snapshot.Purpose == CoreAiServicePurpose.Embedding));
        var degraded = llm == "unavailable" || embedding == "unavailable";
        var runtimeStatus = degraded
            ? "degraded"
            : llm == "checking" || embedding == "checking"
                ? "checking"
                : "available";
        return Task.FromResult(HealthCheckResult.Healthy(
            degraded ? "核心服务正常，部分 AI 能力暂不可用" : "核心服务正常，AI 运行状态来自缓存",
            new Dictionary<string, object>
            {
                ["runtimeStatus"] = runtimeStatus,
                ["llm"] = llm,
                ["embedding"] = embedding,
                ["checkedEntries"] = snapshots.Count
            }));
    }

    private static string Aggregate(IEnumerable<AiServiceReadinessSnapshot> snapshots)
    {
        var states = snapshots.Select(snapshot => snapshot.State).ToList();
        if (states.Any(state => state == AiServiceReadinessState.Available))
            return "available";
        if (states.Any(state => state == AiServiceReadinessState.Checking))
            return "checking";
        if (states.Any(state => state == AiServiceReadinessState.Unavailable))
            return "unavailable";
        return "checking";
    }
}
