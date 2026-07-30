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
    public void BaseSettingsAndDevelopmentDocumentation_ShouldKeepLocalConnectionOutOfGit()
    {
        var repositoryRoot = TestPathHelper.GetRepositoryRoot();
        var baseSettingsPath = Path.Combine(
            repositoryRoot,
            "src",
            "AcceptanceSpecSystem.Api",
            "appsettings.json");
        var baseConnectionString = ReadConnectionString(baseSettingsPath);
        var gitIgnore = File.ReadAllText(Path.Combine(repositoryRoot, ".gitignore"));
        var developmentGuide = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "DEV.md"));

        baseConnectionString.Should().NotBeNullOrWhiteSpace("基础配置仍需要给测试宿主和设计时工厂提供可解析的连接串格式");
        baseConnectionString.Should().NotContain("Database=", "仓库默认配置不能再直接绑定某个固定数据库");
        gitIgnore.Should().Contain("appsettings.Development.json",
            "本地开发连接串和种子口令文件不能进入Git");
        developmentGuide.Should().Contain("src/AcceptanceSpecSystem.Api/appsettings.Development.json");
        developmentGuide.Should().Contain("REPLACE_WITH_LOCAL_PASSWORD",
            "提交的开发文档只能提供无真实凭据的本地配置示例");
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
        WithTemporarySettingsDirectory(directory =>
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                """{"ConnectionStrings":{"DefaultConnection":"Server=localhost;User=root;Password=CHANGE_ME;"}}""");

            ResolveConnectionStringFromDirectory(directory, null)
                .Should().Be("Server=localhost;User=root;Password=CHANGE_ME;");
        });
    }

    [Fact]
    public void DesignTimeFactory_WhenDevelopmentEnvironment_ShouldPreferBranchSpecificDatabase()
    {
        WithTemporarySettingsDirectory(directory =>
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                """{"ConnectionStrings":{"DefaultConnection":"Server=base;User=root;"}}""");
            File.WriteAllText(
                Path.Combine(directory, "appsettings.Development.json"),
                """{"ConnectionStrings":{"DefaultConnection":"Server=development;Database=isolated_test;User=root;"}}""");

            ResolveConnectionStringFromDirectory(directory, "Development")
                .Should().Be("Server=development;Database=isolated_test;User=root;");
        });
    }

    private static string? ResolveConnectionStringFromDirectory(
        string directory,
        string? environmentName)
    {
        var resolveMethod = typeof(AppDbContextFactory)
            .GetMethod(
                "ResolveConnectionStringFromDirectory",
                BindingFlags.NonPublic | BindingFlags.Static);
        resolveMethod.Should().NotBeNull();
        return resolveMethod!.Invoke(null, [directory, environmentName]) as string;
    }

    private static void WithTemporarySettingsDirectory(Action<string> assertion)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "AcceptanceSpecSystem.Data.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            assertion(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
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
