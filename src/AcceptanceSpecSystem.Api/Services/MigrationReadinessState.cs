using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class MigrationReadinessState
{
    private string[] _blockedMigrationIds = [];

    public IReadOnlyList<string> BlockedMigrationIds => Volatile.Read(ref _blockedMigrationIds);

    public bool IsReady => BlockedMigrationIds.Count == 0;

    public void Block(IEnumerable<string> migrationIds) =>
        Volatile.Write(ref _blockedMigrationIds, migrationIds.Distinct(StringComparer.Ordinal).ToArray());
}

public sealed class MigrationReadinessHealthCheck(MigrationReadinessState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["pendingDestructiveMigrationIds"] = state.BlockedMigrationIds.ToArray()
        };

        return Task.FromResult(state.IsReady
            ? HealthCheckResult.Healthy("不存在阻塞启动的破坏性迁移", data)
            : HealthCheckResult.Unhealthy("存在需要显式批准的破坏性迁移", data: data));
    }
}
