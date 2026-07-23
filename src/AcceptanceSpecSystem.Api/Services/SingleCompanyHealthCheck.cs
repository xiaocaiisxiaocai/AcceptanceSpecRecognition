using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class SingleCompanyReadinessState
{
    public bool IsReady { get; private set; } = true;
    public int ActualCount { get; private set; } = 1;

    public void Report(int actualCount)
    {
        ActualCount = actualCount;
        IsReady = actualCount == 1;
    }
}

public sealed class SingleCompanyHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var count = await db.OrgCompanies.CountAsync(cancellationToken);
            var data = new Dictionary<string, object>
            {
                ["expectedCount"] = 1,
                ["actualCount"] = count
            };

            return count == 1
                ? HealthCheckResult.Healthy("单公司根数据不变量满足", data)
                : HealthCheckResult.Unhealthy("单公司根数据不变量不满足", data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("无法验证单公司根数据不变量", ex);
        }
    }
}
