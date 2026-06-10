using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AcceptanceSpecSystem.Data.Context;

/// <summary>
/// 设计时DbContext工厂，用于EF Core迁移命令
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <summary>
    /// 创建DbContext实例
    /// </summary>
    /// <param name="args">命令行参数</param>
    /// <returns>DbContext实例</returns>
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        var connectionString = ResolveConnectionString();
        optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));

        return new AppDbContext(optionsBuilder.Options);
    }

    private static string ResolveConnectionString()
    {
        var environmentConnectionString = ReadEnvironmentConnectionString();
        if (environmentConnectionString.Exists)
        {
            if (!string.IsNullOrWhiteSpace(environmentConnectionString.Value))
            {
                return environmentConnectionString.Value;
            }

            throw new InvalidOperationException(
                "环境变量 ConnectionStrings__DefaultConnection 显式为空，设计时禁止错误回退到 appsettings.json。");
        }

        var apiProjectDirectory = FindApiProjectDirectory();
        var environmentName = ResolveEnvironmentName();
        var baseConnectionString = TryReadConnectionString(Path.Combine(apiProjectDirectory, "appsettings.json"));

        if (!string.IsNullOrWhiteSpace(environmentName))
        {
            var environmentFileConnectionString = TryReadConnectionString(
                Path.Combine(apiProjectDirectory, $"appsettings.{environmentName}.json"));

            if (environmentFileConnectionString.Exists)
            {
                if (!string.IsNullOrWhiteSpace(environmentFileConnectionString.Value))
                {
                    return environmentFileConnectionString.Value;
                }

                throw new InvalidOperationException(
                    $"src/AcceptanceSpecSystem.Api/appsettings.{environmentName}.json 中的 ConnectionStrings:DefaultConnection 显式为空，设计时禁止错误回退到 appsettings.json。");
            }
        }

        if (!string.IsNullOrWhiteSpace(baseConnectionString.Value))
        {
            return baseConnectionString.Value;
        }

        throw new InvalidOperationException(
            "设计时未找到 ConnectionStrings__DefaultConnection。请先设置环境变量，或在 src/AcceptanceSpecSystem.Api/appsettings*.json 中显式配置连接串。");
    }

    private static string? ResolveEnvironmentName()
    {
        var aspnetcoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")?.Trim();
        if (!string.IsNullOrWhiteSpace(aspnetcoreEnvironment))
        {
            return aspnetcoreEnvironment;
        }

        var dotnetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")?.Trim();
        return string.IsNullOrWhiteSpace(dotnetEnvironment) ? null : dotnetEnvironment;
    }

    private static string FindApiProjectDirectory()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var directMatch = Path.Combine(current.FullName, "appsettings.json");
            if (string.Equals(current.Name, "AcceptanceSpecSystem.Api", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(directMatch))
            {
                return current.FullName;
            }

            var nestedMatch = Path.Combine(current.FullName, "src", "AcceptanceSpecSystem.Api");
            if (File.Exists(Path.Combine(nestedMatch, "appsettings.json")))
            {
                return nestedMatch;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("未找到 src/AcceptanceSpecSystem.Api/appsettings.json，无法解析设计时连接串。");
    }

    private static ConnectionStringSetting TryReadConnectionString(string path)
    {
        if (!File.Exists(path))
        {
            return ConnectionStringSetting.Missing;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings) ||
            !connectionStrings.TryGetProperty("DefaultConnection", out var defaultConnection))
        {
            return ConnectionStringSetting.Missing;
        }

        return defaultConnection.ValueKind switch
        {
            JsonValueKind.String => new ConnectionStringSetting(true, defaultConnection.GetString()?.Trim()),
            JsonValueKind.Null => new ConnectionStringSetting(true, null),
            _ => throw new InvalidOperationException(
                $"{path} 中的 ConnectionStrings:DefaultConnection 必须是字符串或 null。")
        };
    }

    private static ConnectionStringSetting ReadEnvironmentConnectionString()
    {
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (!string.Equals(entry.Key?.ToString(), "ConnectionStrings__DefaultConnection", StringComparison.Ordinal))
            {
                continue;
            }

            return new ConnectionStringSetting(true, entry.Value?.ToString()?.Trim());
        }

        return ConnectionStringSetting.Missing;
    }

    private readonly record struct ConnectionStringSetting(bool Exists, string? Value)
    {
        public static ConnectionStringSetting Missing => new(false, null);
    }
}
