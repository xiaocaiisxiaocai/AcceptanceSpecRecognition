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
    public void MatchingKnowledgePage_ShouldGroupSectionsIntoFourTabs()
    {
        var content = ReadRepositoryFile("web/src/views/config/matching-knowledge/index.vue");

        content.Should().Contain("<el-tabs", "匹配知识配置页应按分组切换，避免单页纵向过长");
        content.Should().Contain("label=\"实体别名\"");
        content.Should().Contain("label=\"单位规则\"");
        content.Should().Contain("label=\"字段别名\"");
        content.Should().Contain("label=\"冲突词对\"");
    }

    [Fact]
    public void MatchingKnowledgePage_ShouldSeparateBuiltInAndCustomSections()
    {
        var content = ReadRepositoryFile("web/src/views/config/matching-knowledge/index.vue");

        content.Should().Contain("系统内置（只读）");
        content.Should().Contain("自定义扩展");
        content.Should().Contain("常见电气、机械、芯片半导体术语由系统内置");
        content.Should().Contain("归一系数");
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
