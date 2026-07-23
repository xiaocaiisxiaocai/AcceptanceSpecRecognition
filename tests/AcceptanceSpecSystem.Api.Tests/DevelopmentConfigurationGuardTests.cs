using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class DevelopmentConfigurationGuardTests
{
    [Fact]
    public void TrackedExamples_ShouldNotContainReusableCredentials()
    {
        var values = ReadEnvFile(".env.docker.example");
        values["MYSQL_ROOT_PASSWORD"].Should().BeEmpty();
        values["MYSQL_PASSWORD"].Should().BeEmpty();
        values["JWT_SIGNING_KEY"].Should().BeEmpty();
        values["AUTH_SEED_ADMIN_PASSWORD"].Should().BeEmpty();
        values["AUTH_SEED_COMMON_PASSWORD"].Should().BeEmpty();
        values["MYSQL_DATABASE"].Should().Be("acceptance_spec_db");

        ReadFile("src/AcceptanceSpecSystem.Api/Properties/launchSettings.json")
            .Should().NotContain("ConnectionStrings__DefaultConnection");
    }

    private static IReadOnlyDictionary<string, string> ReadEnvFile(string relativePath)
    {
        return File.ReadLines(GetRepositoryPath(relativePath))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : string.Empty);
    }

    private static string ReadFile(string relativePath)
    {
        return File.ReadAllText(GetRepositoryPath(relativePath));
    }

    private static string GetRepositoryPath(string relativePath)
    {
        return Path.Combine(
            GetRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcceptanceSpecSystem.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位仓库根目录");
    }
}
