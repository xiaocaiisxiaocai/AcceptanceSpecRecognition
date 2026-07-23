using System.Text.Json;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class RepositoryHygieneTests
{
    private static readonly string[] ToolFiles =
    [
        "tools/ParaphraseGenerator/Program.cs",
        "tools/GenerateSemanticTestData.ps1",
        "tools/GenerateParaphrasedExcelViaApi.ps1",
        "tools/GenerateParaphrasedExcel.ps1",
        "tools/FilterGrayZoneSamples.ps1",
        "tools/ExtractGrayZoneSources.ps1"
    ];

    [Fact]
    public void LocalSensitiveArtifacts_ShouldBeIgnored_AndToolsShouldUseSyntheticFixture()
    {
        var ignore = ReadFile(".gitignore");
        ignore.Should().Contain("/huaian*");
        ignore.Should().Contain("/淮安庆鼎*");

        var fixturePath = GetRepositoryPath("tools/Fixtures/synthetic_specs.json");
        File.Exists(fixturePath).Should().BeTrue();

        using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var items = document.RootElement.GetProperty("data").GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThanOrEqualTo(8);
        items.EnumerateArray()
            .Select(item => item.GetProperty("customerName").GetString())
            .Should().OnlyContain(name =>
                name != null && name.StartsWith("示例", StringComparison.Ordinal));

        foreach (var relativePath in ToolFiles)
        {
            var content = ReadFile(relativePath);
            content.Should().Contain("tools/Fixtures/synthetic_specs.json");
            content.Should().NotContain("huaian");
            content.Should().NotContain("淮安庆鼎");
        }
    }

    [Fact]
    public void DockerBuildContext_ShouldExcludeLocalSecretsBackupsAndRealSamples()
    {
        var ignore = ReadFile(".dockerignore").Replace("\r\n", "\n");

        ignore.Should().Contain("/.env*\n");
        ignore.Should().Contain("!/.env*.example");
        ignore.Should().Contain("*.sql");
        ignore.Should().Contain("**/*.sql");
        ignore.Should().Contain("/backups");
        ignore.Should().Contain("**/data-protection-keys");
        ignore.Should().Contain("/output");
        ignore.Should().Contain("/outputs");
        ignore.Should().Contain("/huaian*");
        ignore.Should().Contain("/淮安庆鼎*");
        ignore.Should().NotContain("\noutputs\n");
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
