using System.Reflection;
using System.Text.Json;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Tests.Infrastructure;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

public class DatabaseConnectionConfigurationTests
{
    [Fact]
    public void AppDbContext_ShouldNotExposeHardcodedDefaultConnectionFallback()
    {
        typeof(AppDbContext)
            .GetField("DefaultConnectionString", BindingFlags.Public | BindingFlags.Static)
            .Should().BeNull("当前分支必须显式配置连接串，不能再保留硬编码默认库回退");

        typeof(AppDbContext)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Should().NotContain(ctor => ctor.GetParameters().Length == 0,
                "DbContext 不应再通过无参构造默认连到固定数据库");
    }

    [Fact]
    public void StartupAndDesignTimeFactory_ShouldNotFallbackToHardcodedConnectionString()
    {
        var repositoryRoot = TestPathHelper.GetRepositoryRoot();
        var programPath = Path.Combine(
            repositoryRoot,
            "src",
            "AcceptanceSpecSystem.Api",
            "Program.cs");
        var factoryPath = Path.Combine(
            repositoryRoot,
            "src",
            "AcceptanceSpecSystem.Data",
            "Context",
            "AppDbContextFactory.cs");

        File.ReadAllText(programPath)
            .Should().NotContain("?? AppDbContext.DefaultConnectionString");

        File.ReadAllText(factoryPath)
            .Should().NotContain("AppDbContext.DefaultConnectionString");
    }

    [Fact]
    public void BaseAndDevelopmentAppSettings_ShouldSeparateDefaultAndDevelopmentConnections()
    {
        var repositoryRoot = TestPathHelper.GetRepositoryRoot();
        var baseSettingsPath = Path.Combine(
            repositoryRoot,
            "src",
            "AcceptanceSpecSystem.Api",
            "appsettings.json");
        var developmentSettingsPath = Path.Combine(
            repositoryRoot,
            "src",
            "AcceptanceSpecSystem.Api",
            "appsettings.Development.json");

        var baseConnectionString = ReadConnectionString(baseSettingsPath);
        var developmentConnectionString = ReadConnectionString(developmentSettingsPath);

        baseConnectionString.Should().NotBeNullOrWhiteSpace("基础配置仍需要给测试宿主和设计时工厂提供可解析的连接串格式");
        baseConnectionString.Should().NotContain("Database=", "仓库默认配置不能再直接绑定某个固定数据库");
        developmentConnectionString.Should().NotBeNullOrWhiteSpace("Development 环境需要显式指向当前分支独立数据库");
        developmentConnectionString.Should().Contain("Database=");
        developmentConnectionString.Should().Contain("ai_equivalence_adjudication", because: "当前分支需要使用独立数据库名");
        baseConnectionString.Should().NotBe(developmentConnectionString);
    }

    [Fact]
    public void LaunchSettings_ShouldUseDevelopmentEnvironmentWithoutInjectingConnectionString()
    {
        var repositoryRoot = TestPathHelper.GetRepositoryRoot();
        var launchSettingsPath = Path.Combine(
            repositoryRoot,
            "src",
            "AcceptanceSpecSystem.Api",
            "Properties",
            "launchSettings.json");

        using var document = JsonDocument.Parse(File.ReadAllText(launchSettingsPath));
        var profiles = document.RootElement.GetProperty("profiles");

        foreach (var profileName in new[] { "http", "IIS Express" })
        {
            var environmentVariables = profiles
                .GetProperty(profileName)
                .GetProperty("environmentVariables");

            environmentVariables
                .GetProperty("ASPNETCORE_ENVIRONMENT")
                .GetString()
                .Should().Be("Development");
            environmentVariables
                .TryGetProperty("ConnectionStrings__DefaultConnection", out _)
                .Should().BeFalse("开发连接串应由被忽略的本地配置提供，不能进入已跟踪的启动配置");
        }
    }

    [Fact]
    public void DesignTimeFactory_WhenNoEnvironmentOverride_ShouldUseBaseConnectionWithoutFixedDatabase()
    {
        using var scope = new EnvironmentVariableScope(
            ("ConnectionStrings__DefaultConnection", null),
            ("ASPNETCORE_ENVIRONMENT", null),
            ("DOTNET_ENVIRONMENT", null));

        var resolveMethod = typeof(AppDbContextFactory)
            .GetMethod("ResolveConnectionString", BindingFlags.NonPublic | BindingFlags.Static);

        resolveMethod.Should().NotBeNull();

        var result = resolveMethod!.Invoke(null, null) as string;
        result.Should().NotBeNullOrWhiteSpace();
        result.Should().NotContain("Database=acceptance_spec_ai_equivalence_adjudication_db");
    }

    [Fact]
    public void DesignTimeFactory_WhenDevelopmentEnvironment_ShouldPreferBranchSpecificDatabase()
    {
        using var scope = new EnvironmentVariableScope(
            ("ConnectionStrings__DefaultConnection", null),
            ("ASPNETCORE_ENVIRONMENT", "Development"),
            ("DOTNET_ENVIRONMENT", null));

        var resolveMethod = typeof(AppDbContextFactory)
            .GetMethod("ResolveConnectionString", BindingFlags.NonPublic | BindingFlags.Static);

        resolveMethod.Should().NotBeNull();

        var developmentSettingsPath = Path.Combine(
            TestPathHelper.GetRepositoryRoot(),
            "src",
            "AcceptanceSpecSystem.Api",
            "appsettings.Development.json");
        var result = resolveMethod!.Invoke(null, null) as string;

        result.Should().Be(ReadConnectionString(developmentSettingsPath));
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly (string Name, string? Value)[] _originalValues;

        public EnvironmentVariableScope(params (string Name, string? Value)[] updates)
        {
            _originalValues = updates
                .Select(update => (update.Name, Environment.GetEnvironmentVariable(update.Name)))
                .ToArray();

            foreach (var update in updates)
            {
                Environment.SetEnvironmentVariable(update.Name, update.Value);
            }
        }

        public void Dispose()
        {
            foreach (var original in _originalValues)
            {
                Environment.SetEnvironmentVariable(original.Name, original.Value);
            }
        }
    }

    private static string? ReadConnectionString(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .GetProperty("ConnectionStrings")
            .GetProperty("DefaultConnection")
            .ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => document.RootElement
                .GetProperty("ConnectionStrings")
                .GetProperty("DefaultConnection")
                .GetString(),
            _ => throw new InvalidOperationException($"{path} 中的 DefaultConnection 必须是字符串或 null")
        };
    }
}
