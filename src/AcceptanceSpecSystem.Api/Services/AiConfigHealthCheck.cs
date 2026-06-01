using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class AiConfigHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AiConfigHealthCheck(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var enabledConfigs = await db.AiServiceConfigs
                .AsNoTracking()
                .Where(config => !config.IsDisabled)
                .Select(config => new
                {
                    config.Purpose,
                    config.LlmModel,
                    config.EmbeddingModel
                })
                .ToListAsync(cancellationToken);

            var llmCount = enabledConfigs.Count(config =>
                config.Purpose == AiServicePurpose.Llm ||
                (config.Purpose != AiServicePurpose.Embedding &&
                 !string.IsNullOrWhiteSpace(config.LlmModel) &&
                 string.IsNullOrWhiteSpace(config.EmbeddingModel)));
            var embeddingCount = enabledConfigs.Count(config =>
                config.Purpose == AiServicePurpose.Embedding ||
                (config.Purpose != AiServicePurpose.Llm &&
                 !string.IsNullOrWhiteSpace(config.EmbeddingModel) &&
                 string.IsNullOrWhiteSpace(config.LlmModel)));

            return HealthCheckResult.Healthy(
                "AI 配置表可读取",
                new Dictionary<string, object>
                {
                    ["enabled"] = enabledConfigs.Count,
                    ["llm"] = llmCount,
                    ["embedding"] = embeddingCount
                });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("AI 配置表不可读取", ex);
        }
    }
}
