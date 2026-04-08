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
        content.Should().Contain("label=\"实体组\"");
        content.Should().Contain("label=\"单位规则\"");
        content.Should().Contain("label=\"字段组\"");
        content.Should().Contain("label=\"冲突组\"");
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
    public void MatchingKnowledgePage_ShouldUseGroupedAuthoringLabels()
    {
        var content = ReadRepositoryFile("web/src/views/config/matching-knowledge/index.vue");

        content.Should().Contain("实体组");
        content.Should().Contain("单位组");
        content.Should().Contain("字段组");
        content.Should().Contain("左冲突组");
        content.Should().Contain("右冲突组");
        content.Should().Contain("首项作为标准值");
        content.Should().NotContain("label=\"标准实体\"");
        content.Should().NotContain("label=\"标准字段\"");
    }

    [Fact]
    public void MatchingKnowledgePage_ShouldExplainDraftMergeFeedback()
    {
        var content = ReadRepositoryFile("web/src/views/config/matching-knowledge/index.vue");

        content.Should().Contain("并入已有组");
        content.Should().Contain("新建组");
        content.Should().Contain("候选与现有分组冲突未导入");
    }

    [Fact]
    public void MatchingKnowledgePage_ShouldUseRowLevelEditMode()
    {
        var content = ReadRepositoryFile("web/src/views/config/matching-knowledge/index.vue");

        content.Should().Contain("编辑");
        content.Should().Contain("完成");
        content.Should().Contain("取消");
        content.Should().Contain("startGroupRowEdit");
        content.Should().Contain("cancelGroupRowEdit");
        content.Should().Contain("startConflictGroupRowEdit");
        content.Should().Contain("cancelConflictGroupRowEdit");
        content.Should().Contain("v-if=\"row.editing\"");
    }

    [Fact]
    public void MatchingKnowledgePage_ShouldProvideTabSearchFilters()
    {
        var content = ReadRepositoryFile("web/src/views/config/matching-knowledge/index.vue");

        content.Should().Contain("entitySearchQuery");
        content.Should().Contain("unitSearchQuery");
        content.Should().Contain("fieldSearchQuery");
        content.Should().Contain("conflictSearchQuery");
        content.Should().Contain("filteredEntityGroupRows");
        content.Should().Contain("filteredUnitGroupRows");
        content.Should().Contain("filteredFieldGroupRows");
        content.Should().Contain("filteredConflictGroupRows");
        content.Should().Contain("matchesGroupSearch");
        content.Should().Contain("matchesConflictGroupSearch");
        content.Should().Contain("placeholder=\"搜索当前 Tab\"");
    }

    [Fact]
    public void MatchingKnowledgePage_SearchBar_ShouldStickInsideScrollableCardBody()
    {
        var normalizedContent = ReadRepositoryFile("web/src/views/config/matching-knowledge/index.vue")
            .Replace("\r\n", "\n");

        normalizedContent.Should().Contain(
            """
            .tab-search-row {
              position: sticky;
              top: 0;
            """,
            "搜索栏在卡片 body 内滚动时应固定在顶部");

        normalizedContent.Should().Contain(
            "z-index: 10;",
            "吸顶搜索栏需要位于表格内容之上");

        normalizedContent.Should().Contain(
            "background: var(--el-bg-color);",
            "吸顶搜索栏需要有背景色覆盖滚动内容");
    }

    [Fact]
    public void MatchingKnowledgePage_ShouldNotRenderUnitFactorSection()
    {
        var content = ReadRepositoryFile("web/src/views/config/matching-knowledge/index.vue");

        content.Should().NotContain("单位换算", "单位换算仅兼容保留，不应在页面展示");
        content.Should().NotContain("filteredUnitFactorRows", "页面不应再渲染单位换算列表");
        content.Should().NotContain("startNumberRowEdit", "页面不应再提供单位换算编辑交互");
        content.Should().Contain("hiddenUnitFactors", "前端保存时仍需保留后端兼容字段，避免保存后丢失历史数据");
    }

    [Fact]
    public void MatchingKnowledgePage_UnitRulesSearch_ShouldRenderInsideFirstCard()
    {
        var content = ReadRepositoryFile("web/src/views/config/matching-knowledge/index.vue");
        var unitRulesStart = content.IndexOf("<el-tab-pane label=\"单位规则\"", StringComparison.Ordinal);
        var fieldGroupStart = content.IndexOf("<el-tab-pane label=\"字段组\"", StringComparison.Ordinal);

        unitRulesStart.Should().BeGreaterThanOrEqualTo(0, "应存在单位规则 Tab");
        fieldGroupStart.Should().BeGreaterThan(unitRulesStart, "单位规则 Tab 后应存在字段组 Tab");

        var unitRulesSection = content.Substring(unitRulesStart, fieldGroupStart - unitRulesStart);
        var firstCardIndex = unitRulesSection.IndexOf("<el-card class=\"knowledge-card\">", StringComparison.Ordinal);
        var searchRowIndex = unitRulesSection.IndexOf("<div class=\"tab-search-row\">", StringComparison.Ordinal);

        firstCardIndex.Should().BeGreaterThanOrEqualTo(0, "单位规则 Tab 中应渲染知识卡片");
        searchRowIndex.Should().BeGreaterThanOrEqualTo(0, "单位规则 Tab 中应渲染搜索栏");
        firstCardIndex.Should().BeLessThan(searchRowIndex, "单位规则搜索栏应位于首张卡片内部，而不是卡片外层顶部");
    }

    [Fact]
    public void MatchingKnowledgePage_UnitRulesCards_ShouldScrollInsideCardBodyLikeEntityGroups()
    {
        var normalizedContent = ReadRepositoryFile("web/src/views/config/matching-knowledge/index.vue")
            .Replace("\r\n", "\n");

        normalizedContent.Should().Contain(
            """
            :deep(.knowledge-tabs > .el-tabs__content > .el-tab-pane.multi-card-pane) {
              display: flex;
              flex-direction: column;
              overflow: hidden;
            }
            """,
            "单位规则页签应像实体组一样由外层弹性容器承载，而不是整页滚动");

        normalizedContent.Should().Contain(
            """
            :deep(
              .knowledge-tabs
                > .el-tabs__content
                > .el-tab-pane.multi-card-pane
                > .knowledge-grid
                > .knowledge-card
                > .el-card__body
            ) {
              flex: 1;
              min-height: 0;
              overflow: auto;
            }
            """,
            "单位规则卡片应像实体组一样让 body 负责内部滚动，从而保持卡片头部固定");
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
