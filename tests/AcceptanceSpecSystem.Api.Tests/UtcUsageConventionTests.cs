using System.Text.RegularExpressions;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class UtcUsageConventionTests
{
    [Fact]
    public void RepositoryCode_ShouldNotUseDateTimeNow()
    {
        var repositoryRoot = GetRepositoryRoot();
        var matches = Directory
            .EnumerateFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => IsTrackedSourceFile(repositoryRoot, path))
            .SelectMany(path => FindDateTimeNowUsages(repositoryRoot, path))
            .ToList();

        matches.Should().BeEmpty("仓库代码应统一使用 UTC 时间，命中项: {0}", string.Join(Environment.NewLine, matches));
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AcceptanceSpecSystem.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("未找到仓库根目录");
    }

    private static bool IsTrackedSourceFile(string repositoryRoot, string path)
    {
        var relativePath = Path.GetRelativePath(repositoryRoot, path);
        if (!relativePath.StartsWith("src", StringComparison.OrdinalIgnoreCase) &&
            !relativePath.StartsWith("tests", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !relativePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
               !relativePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> FindDateTimeNowUsages(string repositoryRoot, string path)
    {
        var relativePath = Path.GetRelativePath(repositoryRoot, path);
        var lines = File.ReadAllLines(path);
        for (var index = 0; index < lines.Length; index++)
        {
            if (Regex.IsMatch(lines[index], @"\bDateTime\.Now\b", RegexOptions.CultureInvariant))
                yield return $"{relativePath}:{index + 1}";
        }
    }
}
