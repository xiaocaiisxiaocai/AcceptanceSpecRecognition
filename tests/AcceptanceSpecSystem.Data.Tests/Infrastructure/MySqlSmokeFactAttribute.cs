namespace AcceptanceSpecSystem.Data.Tests.Infrastructure;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class MySqlSmokeFactAttribute : FactAttribute
{
    public MySqlSmokeFactAttribute()
    {
        var enabled = Environment.GetEnvironmentVariable(MySqlMigrationTestDatabase.EnableEnvironmentVariableName)?.Trim();
        if (!string.Equals(enabled, "1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"未设置 {MySqlMigrationTestDatabase.EnableEnvironmentVariableName}=true，跳过真实 MySQL 迁移烟测。";
            return;
        }

        var baseConnection = Environment.GetEnvironmentVariable(MySqlMigrationTestDatabase.BaseConnectionEnvironmentVariableName)?.Trim();
        if (string.IsNullOrWhiteSpace(baseConnection))
        {
            Skip = $"未设置 {MySqlMigrationTestDatabase.BaseConnectionEnvironmentVariableName}，跳过真实 MySQL 迁移烟测。";
        }
    }
}
