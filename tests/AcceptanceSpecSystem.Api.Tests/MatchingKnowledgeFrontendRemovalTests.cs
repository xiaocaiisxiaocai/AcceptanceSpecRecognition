using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class MatchingKnowledgeFrontendRemovalTests
{
    [Fact]
    public void ConfigRoute_ShouldNotExposeMatchingKnowledgePage()
    {
        var content = ReadRepositoryFile("web/src/router/modules/config.ts");

        content.Should().NotContain("/config/matching-knowledge");
        content.Should().NotContain("MatchingKnowledgeConfig");
        content.Should().NotContain("@/views/config/matching-knowledge/index.vue");
        content.Should().NotContain("config-matching-knowledge");
    }

    [Fact]
    public void Frontend_ShouldDeleteMatchingKnowledgeViewAndApi()
    {
        var repositoryRoot = GetRepositoryRoot();
        var removedFiles = new[]
        {
            "web/src/views/config/matching-knowledge/index.vue",
            "web/src/views/config/matching-knowledge/components/MatchingKnowledgeDraftDialog.vue",
            "web/src/api/matching-knowledge.ts"
        };

        foreach (var relativePath in removedFiles)
        {
            File.Exists(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Should().BeFalse($"{relativePath} 应在 AI 等价裁决分支中移除");
        }
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        return File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AcceptanceSpecSystem.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("未找到仓库根目录");
    }
}
