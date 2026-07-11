using System.Data.Common;

namespace AcceptanceSpecSystem.Api.Options;

/// <summary>
/// 识别示例配置和常见占位符，防止长度合规但公开可知的值进入生产环境。
/// </summary>
public static class ProductionSecretGuard
{
    public static bool IsKnownPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized.StartsWith("replace_with_", StringComparison.Ordinal) ||
               normalized.StartsWith("changethis", StringComparison.Ordinal) ||
               normalized.Contains("change_me", StringComparison.Ordinal) ||
               normalized.StartsWith("__required", StringComparison.Ordinal) ||
               normalized.StartsWith("your_", StringComparison.Ordinal) ||
               normalized.StartsWith("devonly_", StringComparison.Ordinal) ||
               normalized.StartsWith("example_", StringComparison.Ordinal) ||
               normalized.StartsWith("sample_", StringComparison.Ordinal) ||
               normalized is "password" or "secret";
    }

    public static bool HasKnownPlaceholderPassword(string connectionString)
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };

        foreach (var key in new[] { "Password", "Pwd" })
        {
            if (builder.TryGetValue(key, out var value))
            {
                return IsKnownPlaceholder(value?.ToString());
            }
        }

        return false;
    }
}
