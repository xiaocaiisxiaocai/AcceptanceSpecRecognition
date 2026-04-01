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
        content.Should().Contain("permissions: getPagePermission(\"config-matching-knowledge\")");
    }

    [Fact]
    public void MatchingKnowledgePage_ShouldUseExpectedPermissionCodes()
    {
        var content = ReadRepositoryFile("web/src/views/config/matching-knowledge/index.vue");

        content.Should().Contain("btn:matching-knowledge:update");
        content.Should().Contain("btn:matching-knowledge:reset");
        content.Should().Contain("btn:matching-knowledge:generate-draft");
        content.Should().Contain("getMatchingKnowledge");
        content.Should().Contain("updateMatchingKnowledge");
        content.Should().Contain("clearMatchingKnowledge");
        content.Should().Contain("restoreDefaultMatchingKnowledge");
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
    public void MatchingKnowledgePage_ShouldUseSingleEditableConfigView()
    {
        var content = ReadRepositoryFile("web/src/views/config/matching-knowledge/index.vue");

        content.Should().NotContain("系统内置（只读）");
        content.Should().NotContain("自定义扩展");
        content.Should().Contain("当前生效配置");
        content.Should().Contain("清空当前配置");
        content.Should().Contain("恢复默认配置");
    }

    [Fact]
    public void MatchingKnowledgePage_ShouldExposeAiDraftGenerationFlow()
    {
        var pageContent = ReadRepositoryFile("web/src/views/config/matching-knowledge/index.vue");
        var dialogContent = ReadRepositoryFile("web/src/views/config/matching-knowledge/components/MatchingKnowledgeDraftDialog.vue");

        pageContent.Should().Contain("AI 生成候选");
        pageContent.Should().Contain("MatchingKnowledgeDraftDialog");
        dialogContent.Should().Contain("历史验规");
        dialogContent.Should().Contain("getAiServiceList");
        dialogContent.Should().Contain("llmServiceId");
        dialogContent.Should().Contain("automatic-dropdown=\"false\"");
        dialogContent.Should().Contain("导入时间");
        dialogContent.Should().Contain("全选");
        dialogContent.Should().Contain("取消全选");
        dialogContent.Should().NotContain("<el-form-item label=\"客户\">");
        dialogContent.Should().NotContain("<el-form-item label=\"制程\">");
        dialogContent.Should().NotContain("<el-form-item label=\"机型\">");
        dialogContent.Should().NotContain("<el-table-column prop=\"customerName\"");
        dialogContent.Should().NotContain("<el-table-column prop=\"processName\"");
        dialogContent.Should().NotContain("<el-table-column prop=\"machineModelName\"");
        dialogContent.Should().NotContain("粘贴文本");
        dialogContent.Should().NotContain("已上传文档");
        dialogContent.Should().NotContain("临时上传文档");
        dialogContent.Should().Contain("导入到当前配置");
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
