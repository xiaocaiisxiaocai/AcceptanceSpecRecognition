using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class MatchingKnowledgeFrontendRegressionTests
{
    [Fact]
    public void ConfigRoute_ShouldExposeMatchingKnowledgePage()
    {
        var content = ReadRepositoryFile("web/src/router/modules/config.ts");

        content.Should().Contain("path: \"/config/matching-knowledge\"");
        content.Should().Contain("name: \"MatchingKnowledgeConfig\"");
        content.Should().Contain("component: () => import(\"@/views/config/matching-knowledge/index.vue\")");
        content.Should().Contain("permissions: [\"page:config:matching-knowledge\"]");
    }

    [Fact]
    public void MatchingKnowledgePage_ShouldUseExpectedPermissionCodes()
    {
        var content = ReadRepositoryFile("web/src/views/config/matching-knowledge/index.vue");

        content.Should().Contain("btn:matching-knowledge:update");
        content.Should().Contain("btn:matching-knowledge:reset");
        content.Should().Contain("getMatchingKnowledge");
        content.Should().Contain("updateMatchingKnowledge");
        content.Should().Contain("resetMatchingKnowledge");
    }

    [Fact]
    public void LegacyTextProcessingPages_ShouldBeRemoved()
    {
        var repositoryRoot = GetRepositoryRoot();
        var removedFiles = new[]
        {
            "web/src/views/config/text-processing/index.vue",
            "web/src/views/other/synonyms/index.vue",
            "web/src/views/other/keywords/index.vue"
        };

        foreach (var relativePath in removedFiles)
        {
            File.Exists(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Should().BeFalse($"{relativePath} 应在新匹配知识配置页落地后删除");
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
