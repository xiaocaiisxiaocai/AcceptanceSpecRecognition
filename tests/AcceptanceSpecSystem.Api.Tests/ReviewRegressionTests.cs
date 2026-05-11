using System.Reflection;
using AcceptanceSpecSystem.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Tests;

public class ReviewRegressionTests
{
    [Fact]
    public void SpecsController_DuplicateGroups_ShouldNotLoadAllSpecsIntoMemory()
    {
        var lines = ReadFile("src/AcceptanceSpecSystem.Api/Controllers/SpecsController.cs");

        lines.Should().NotContain(line =>
            line.Contains("GetAllWithCustomerAndProcessAsync()", StringComparison.Ordinal) &&
            line.Contains("AcceptanceSpecs", StringComparison.Ordinal),
            "重复分组接口应将作用域和筛选下推到数据库，而不是先全表加载");
    }

    [Fact]
    public void ProcessesController_GetProcessSpecs_ShouldNotLoadAllSpecsIntoMemory()
    {
        var lines = ReadFile("src/AcceptanceSpecSystem.Api/Controllers/ProcessesController.cs");

        lines.Should().NotContain(line =>
            line.Contains("GetAllWithCustomerAndProcessAsync()", StringComparison.Ordinal) &&
            line.Contains("AcceptanceSpecs", StringComparison.Ordinal),
            "按制程查询规格应复用数据库分页查询，而不是先全表加载");
    }

    [Fact]
    public void AiServicesController_ShouldDependOnHttpClientFactory()
    {
        var constructors = typeof(AcceptanceSpecSystem.Api.Controllers.AiServicesController)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        constructors.Should().ContainSingle();
        constructors[0]
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Contain(typeof(IHttpClientFactory), "AI 服务探测应通过 IHttpClientFactory 创建 HttpClient");
    }

    [Fact]
    public void AcceptanceSpecRepository_ShouldNotExposeLegacyGroupSummaryMethod()
    {
        typeof(AcceptanceSpecSystem.Data.Repositories.IAcceptanceSpecRepository)
            .GetMethod("GetGroupSummaryAsync")
            .Should()
            .BeNull("旧的无作用域分组接口已被新查询接口替代，不应继续暴露");

        typeof(AcceptanceSpecSystem.Data.Repositories.AcceptanceSpecRepository)
            .GetMethod("GetGroupSummaryAsync")
            .Should()
            .BeNull("仓储实现中的旧分组方法应与接口一起删除，避免形成陈旧 API");
    }

    [Fact]
    public void SourceFiles_ShouldNotContainLegacyDefaultPasswords()
    {
        var sourceFiles = new[]
        {
            "src/AcceptanceSpecSystem.Api/Services/AuthUserSeedService.cs",
            "web/src/views/login/index.vue"
        };

        foreach (var relativePath in sourceFiles)
        {
            var content = File.ReadAllText(Path.Combine(GetRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
            content.Should().NotContain("Admin@123456", $"{relativePath} 不应再硬编码默认管理员密码");
            content.Should().NotContain("Common@123456", $"{relativePath} 不应再硬编码默认普通用户密码");
        }
    }

    [Fact]
    public void ScoreDetailDialog_ShouldClearInlineDiffCache()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailDialog.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("inlineDiffCache.clear()", "匹配详情弹窗关闭或切换数据时应清理 diff 缓存");
    }

    [Fact]
    public void ScoreDetailDialog_ShouldHighlightSourceVsBestMatchDifference()
    {
        var dialogContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailDialog.vue".Replace('/', Path.DirectorySeparatorChar)));
        var diffSectionContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailDiffSection.vue".Replace('/', Path.DirectorySeparatorChar)));

        dialogContent.Should().Contain("sourceBestRows", "父组件应继续向差异区块透传源项与最佳匹配的 diff 数据");
        diffSectionContent.Should().Contain("源项与最佳匹配差异", "应由差异区块组件渲染专门的高亮区域，避免用户自行肉眼比对");
        diffSectionContent.Should().Contain("v-html=\"row.leftHtml\"", "差异区块应复用现有 inline diff 高亮渲染");
        diffSectionContent.Should().Contain("v-html=\"row.rightHtml\"", "差异区块应同时渲染源项与最佳匹配的高亮结果");
    }

    [Fact]
    public void ScoreDetailDialog_ShouldKeepScrollableContentWithinViewport()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailDialog.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("width=\"1200px\"",
            "匹配详情弹窗应提供更宽的默认宽度，减少长规格文本过早换行");
        content.Should().Contain("class=\"score-detail-dialog\"",
            "匹配详情弹窗应为根容器声明独立样式类，避免内容过长时超出视口");
        content.Should().Contain(":deep(.score-detail-dialog)",
            "匹配详情弹窗应通过根类约束整体高度，而不是只让内部内容自然撑开");
        content.Should().Contain("max-height: 90vh",
            "匹配详情弹窗应限制在视口高度内，避免底部按钮和长文本被裁掉");
        content.Should().Contain("el-dialog__body",
            "匹配详情弹窗应显式控制 body 区域布局，确保滚动区接管长内容");
        content.Should().Contain("min-height: 0",
            "弹性布局中的滚动容器需要显式最小高度，才能正常向下滚动");
    }

    [Fact]
    public void ScoreDetailDialog_ShouldSeparateDecisionViewAndTechnicalView()
    {
        var dialogContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailDialog.vue".Replace('/', Path.DirectorySeparatorChar)));
        var decisionSectionPath = Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailDecisionSummarySection.vue".Replace('/', Path.DirectorySeparatorChar));

        File.Exists(decisionSectionPath).Should().BeTrue("匹配详情弹窗应拆出面向客户的结论区块，避免继续把业务判断和技术细节混在一起");
        dialogContent.Should().Contain("ScoreDetailDecisionSummarySection",
            "弹窗壳应显式组合面向客户的结论区块组件");
        dialogContent.Should().Contain("<el-tabs",
            "匹配详情弹窗应通过 Tab 分离客户视图和开发视图");
        dialogContent.Should().Contain("label=\"匹配结论\"",
            "第一个 Tab 应聚焦业务用户的一眼判断");
        dialogContent.Should().Contain("label=\"技术详情\"",
            "第二个 Tab 应保留开发和实施需要的技术证据");
    }

    [Fact]
    public void ScoreDetailDialog_ShouldGroupTechnicalDetailsIntoTagViews()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailDialog.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("const activeTechnicalTag = ref(\"overview\")",
            "技术详情应默认落在概览视图，而不是一次摊开所有技术块");
        content.Should().Contain("<el-check-tag",
            "技术详情应通过标签切换不同信息块，减少整屏堆叠");
        content.Should().Contain("技术概览",
            "技术详情应先给出一个最小必要的概览入口");
        content.Should().Contain("源项差异",
            "源项与最佳匹配差异应独立成一个标签视图");
        content.Should().Contain("候选对比",
            "候选对比应独立成一个标签视图");
        content.Should().Contain("候选列表",
            "候选列表不应再默认和其他内容同时铺开");
    }

    [Fact]
    public void ScoreDetailDecisionSummarySection_ShouldExposeCustomerFriendlyDecisionCues()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailDecisionSummarySection.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("一句话结论",
            "客户视角应先给出一句话结论，而不是要求用户自己读完整页再推断");
        content.Should().Contain("建议动作",
            "客户视角应明确给出下一步建议，而不是只展示技术标签");
        content.Should().Contain("风险级别",
            "客户视角应把风险压缩成易读等级，方便客户快速判断");
        content.Should().Contain("请重点确认",
            "客户视角应明确指出需要客户重点确认的内容");
    }

    [Fact]
    public void ScoreDetailDecisionSummarySection_ShouldAvoidRepeatingTechnicalOrDuplicateBlocks()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailDecisionSummarySection.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("系统判断",
            "客户视角不应重复展示和一句话结论含义重叠的卡片");
        content.Should().NotContain("最终决策",
            "客户视角不应直接暴露开发术语");
        content.Should().NotContain("最佳得分",
            "客户视角不应把评分指标放在主决策区");
        content.Should().NotContain("关键依据",
            "客户视角应进一步收敛内容，避免形成第二层说明书");
        content.Should().NotContain("风险提醒",
            "风险应合并进更短的判断区，而不是再拆出独立大面板");
    }

    [Fact]
    public void ScoreDetailDecisionSummarySection_ShouldHighlightSourceAndRecommendedDifferences()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailDecisionSummarySection.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("v-html=\"getComparisonHtml(",
            "客户视角中的源项与推荐项应直接展示差异高亮，而不是只显示纯文本");
        content.Should().Contain("inline-mark-old",
            "差异高亮应继续标记源项独有内容");
        content.Should().Contain("inline-mark-new",
            "差异高亮应继续标记推荐项新增内容");
    }

    [Fact]
    public void MatchPreviewTable_ShouldUseUnifiedCustomerFacingDecisionBuckets()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/MatchPreviewTable.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("100%精确直达 (",
            "主表应继续把完全精确命中的可填充项单独分组，方便客户快速确认");
        content.Should().Contain("AI/普通可填充 (",
            "主表应单独展示非精确直达但仍可填充的项，满足客户区分精确命中与 AI/普通填充的需求");
        content.Should().Contain("需要确认 (",
            "主表应单独统计需要客户确认的行，避免把不同状态混成一类");
        content.Should().Contain("不建议填充 (",
            "主表应把拒绝或冲突项单独归类，方便客户快速避开风险行");
        content.Should().Contain("无匹配 (",
            "主表应把无匹配单独列出，而不是并入需关注造成口径混乱");
        content.Should().NotContain("可直接填充 (",
            "当前主表已拆分为精确直达与 AI/普通可填充两个分组，不应再退回单一口径");
        content.Should().NotContain("自动采用 (",
            "客户主表不应继续使用自动采用这类技术性决策文案");
        content.Should().NotContain("需关注 (",
            "原有需关注统计口径过粗，不能再作为主筛选文案");
        content.Should().NotContain("const imperfect = total - perfect;",
            "需要确认不应再通过总数减高置信数来倒推，否则会把无匹配和拒绝项混进来");
    }

    [Fact]
    public void MatchPreviewTable_ShouldUseAuthoritativeDecisionForFillRecommendation()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/MatchPreviewTable.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("return tableState.fillRecommendation;",
            "主表的填充建议应以后端 authoritative decision 为准，不应再本地二次改写");
        content.Should().NotContain("if (hasCustomerVisibleRisk(item)) return \"review\";",
            "主表不应再因为本地置信度/歧义/issues 信号把 autoApply 二次降级为 review");
        content.Should().NotContain("硬冲突拦截",
            "AI-only 口径下主表状态文案不应继续假装存在本地硬冲突门禁");
    }

    [Fact]
    public void MatchPreviewTable_ShouldShowWhyAmbiguousRowsAreMarked()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/MatchPreviewTable.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("Top1/Top2分差",
            "主表应直接展示高歧义依据，避免用户只看到标签却不知道为什么");
        content.Should().Contain("歧义阈值",
            "主表应把当前阈值一并展示，方便判断是否只是分差过小");
        content.Should().Contain("formatOptionalPercent",
            "主表应复用格式化函数展示分差和阈值，而不是只显示布尔状态");
    }

    [Fact]
    public void MatchPreviewTable_ShouldUseNeutralReviewedStatusCopy()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/MatchPreviewTable.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("AI判定可采用",
            "复核状态应直接展示 authoritative 决策结果，而不是继续暴露旧的复核分指标");
        content.Should().Contain("复核后待确认",
            "AI 复核完成但仍需人工确认时，应明确显示待确认状态");
        content.Should().NotContain("模型复核分",
            "主表不应再展示旧的模型复核分字段");
        content.Should().NotContain("LLM复核",
            "主表状态文案不应再把旧的 LLM 复核指标当成最终状态标签");
        content.Should().NotContain("分通过",
            "主表不应继续显示“100分通过”这类强结论文案");
        content.Should().NotContain("分未通过",
            "主表不应继续显示“xx分未通过”这类容易与最终填充建议混淆的文案");
    }

    [Fact]
    public void ScoreDetailBestMatchSection_ShouldLabelLlmMetricAsNeutralReviewScore()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailBestMatchSection.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("label: \"AI 等价裁决\"",
            "技术概览应改为展示 AI 等价裁决，而不是继续保留旧的复核分指标");
        content.Should().NotContain("label: \"模型复核分\"",
            "技术概览不应继续使用旧的模型复核分标题");
        content.Should().NotContain("label: \"LLM复核\"",
            "技术概览不应继续使用带结论感的旧指标标题");
    }

    [Fact]
    public void ScoreDetailDecisionSummarySection_ShouldKeepAuthoritativeRecommendationAndExposeDifferencesSeparately()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailDecisionSummarySection.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("const hasCustomerVisibleDifference = computed(() =>",
            "详情页不应再以本地差异信号覆盖 authoritative recommendation");
        content.Should().NotContain("description: \"存在差异，请先确认\"",
            "详情页不应再把 autoApply 二次改写为本地降级文案");
        content.Should().NotContain("return \"核对差异后再填充\";",
            "建议动作应直接沿用 authoritative recommendation，差异提示应留在 checklist");
        content.Should().Contain("const focusChecklist = computed(() =>",
            "详情页仍应保留差异/问题 checklist，供用户人工核对");
    }

    [Fact]
    public void ScoreDetailDecisionSummarySection_ShouldReduceRepeatedCustomerCopy()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailDecisionSummarySection.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("<el-alert",
            "客户视角不应再叠加顶部提示条，否则会和结论卡重复表达同一件事");
        content.Should().NotContain("补充说明",
            "客户视角应把重复说明压缩进重点确认区，不应再单独保留补充说明面板");
        content.Should().NotContain("核对差异后再填充",
            "旧差异确认话术已经下线，不应继续覆盖当前 AI 结论");
        content.Should().NotContain("存在差异，请先确认",
            "旧结论短句已经下线，不应继续出现在详情摘要里");
        content.Should().Contain("请结合详情表格核对源项与推荐项差异",
            "重点确认区仍应保留一条简洁、面向用户的差异核对提示");
    }

    [Fact]
    public void ScoreDetailDecisionSummarySection_ShouldPlaceAcceptanceAndRemarkOnRecommendedSide()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailDecisionSummarySection.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("label=\"推荐验收标准\"",
            "验收标准属于推荐规格内容，应明确标注在推荐侧");
        content.Should().Contain("label=\"推荐备注\"",
            "备注属于推荐规格内容，应明确标注在推荐侧");
        content.Should().NotContain("label=\"验收标准\"",
            "客户视角不应再把推荐字段显示成未归属的通用字段");
        content.Should().NotContain("label=\"备注\"",
            "客户视角不应再把推荐字段显示成未归属的通用字段");
    }

    [Fact]
    public void ScoreDetailBestMatchSection_ShouldGroupNarrativeTextIntoStructuredSummary()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailBestMatchSection.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("分析摘要",
            "技术详情中的长文本应汇总到统一摘要区，而不是散落成多个自然段");
        content.Should().Contain("meta-tag-list",
            "最佳匹配区应把核心状态压缩成标签行，方便快速扫读");
        content.Should().Contain("metric-grid",
            "最佳匹配区应把分数和关键指标压缩成紧凑指标块");
        content.Should().Contain("summary-list",
            "技术摘要应改成可扫读的结构化列表");
        content.Should().Contain("歧义阈值",
            "技术概览应直接展示高歧义判定阈值，便于开发核对原因");
        content.Should().Contain("Top1/Top2分差",
            "技术概览应直接展示当前分差，避免只给高歧义标签");
        content.Should().NotContain("证据摘要",
            "最佳匹配区不应再单独堆一个证据摘要文本块");
        content.Should().NotContain("重排摘要",
            "最佳匹配区不应再单独堆一个重排摘要文本块");
        content.Should().NotContain("LLM复核过程",
            "最佳匹配区不应默认直接摊开整段复核过程");
    }

    [Fact]
    public void ScoreDetailDiffSection_ShouldUseConciseTechnicalHints()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailDiffSection.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("Top1 对 Top2/Top3",
            "候选对比区应把用途压缩成短标题，方便开发快速扫读");
        content.Should().Contain("红=Top1独有",
            "原文对照区应把说明压缩成极短图例");
        content.Should().NotContain("用于判断 Top1 与 Top2/Top3 为什么接近或拉开",
            "候选对比区不应继续保留成句说明");
        content.Should().NotContain("左侧固定为 Top1，右侧可切换 Top2 / Top3",
            "差异区不应继续保留长段操作说明");
        content.Should().NotContain("采用左右并排对照，绿色表示候选新增内容",
            "原文对照区不应继续保留整句解释");
    }

    [Fact]
    public void BatchTableConfig_ShouldUseFirstExcelTableAsInitialTemplateOnly()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/BatchTableConfig.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("const applyPrimaryExcelConfigToOthers = (",
            "Excel 多表配置应抽出首表自动同步逻辑");
        content.Should().Contain("if (props.isExcel && index === 0)",
            "修改首表配置时应自动同步其他表格");
        content.Should().Contain("首表配置会作为默认值带出其他表",
            "页面应明确说明首表只负责带出默认配置");
        content.Should().Contain("可单独调整",
            "页面应明确提示其他表仍可按各自结构单独调整");
        content.Should().Contain("默认参考首表",
            "其他表格应展示自己默认继承首表配置，而不是永久锁定跟随");
        content.Should().NotContain(":disabled=\"isSyncedExcelTable(idx)\"",
            "其他表格不应继续被禁用，否则用户无法按各 Sheet 实际结构修正");
        content.Should().NotContain("当前表格字段与行配置跟随首表同步。",
            "其他表格提示文案不应再表达为强制同步关系");
        content.Should().NotContain("应用到其他表格",
            "Excel 智能填充不应再保留手动应用到其他表格的入口");
        content.Should().NotContain("复制此表字段配置",
            "Excel 智能填充不应再保留逐表复制字段配置的入口");
    }

    [Fact]
    public void BatchReplyPage_ShouldUseStepFileAndSheetTabs_ForIndependentTableConfiguration()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/batch-reply/index.vue".Replace('/', Path.DirectorySeparatorChar)));
        var workspaceContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/BatchTableConfig.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("来源文件",
            "批量回复页面顶层步骤应改成来源文件，而不是继续停留在来源配置语义");
        content.Should().Contain("目标文件",
            "批量回复页面顶层步骤应改成目标文件，而不是继续停留在目标配置语义");
        content.Should().Contain("执行结果",
            "批量回复页面应提供单独结果 Tab，避免执行后仍挤在配置区域里");
        content.Should().NotContain("来源配置",
            "批量回复页面不应继续保留旧的来源配置 Tab 文案");
        content.Should().NotContain("目标配置",
            "批量回复页面不应继续保留旧的目标配置 Tab 文案");
        content.Should().Contain("来源表",
            "目标表配置应显式允许选择对应来源表");
        workspaceContent.Should().Contain("sheet-tabs",
            "批量回复页面应显式使用 Sheet/表格级 Tab，而不是继续使用表格卡片堆叠");
        content.Should().NotContain("当前表回写预览",
            "独立预检查/预览区域应被移除，预览应回到当前 Sheet/表格上下文");
        content.Should().NotContain("请在当前目标文件的表格卡片上点击“预览回写”",
            "页面不应再要求用户回到表格卡片上触发独立预检查区域");
    }

    [Fact]
    public void BatchReplyPage_ShouldUseEnterpriseWorkbenchVisualShell()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/batch-reply/index.vue".Replace('/', Path.DirectorySeparatorChar)));
        var workspaceContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/BatchTableConfig.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("page-shell",
            "批量回复页面应显式使用工作台页壳类名，避免继续沿用宣传横幅语义");
        content.Should().Contain("page-header",
            "批量回复页面应使用企业工作台页头，而不是普通横幅块");
        content.Should().Contain("rule-strip",
            "批量回复页面应将规则说明收敛成企业提示条");
        content.Should().Contain("workflow-panel",
            "批量回复页面应给步骤导航提供独立的工作流容器");
        content.Should().Contain("file-stage-panel",
            "批量回复页面应给文件工作区提供稳定的企业面板容器");
        workspaceContent.Should().Contain("workspace-shell",
            "Sheet 工作区应有稳定的工作台壳层，而不是只剩默认 tabs + 卡片");
        workspaceContent.Should().Contain("config-section",
            "Sheet 工作区中的配置区域应按企业表单分区展示");
    }

    [Fact]
    public void BatchReplyPage_ShouldBatchTargetUploadsInsteadOfPostingEachFileSeparately()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/batch-reply/index.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("pendingTargetUploadFiles",
            "目标文件上传应显式维护待上传队列，避免 on-change 命中时逐文件并发请求");
        content.Should().Contain("uploadBatchReplyTargets(sourceSessionId.value, pendingFiles)",
            "目标文件应合并为一次上传请求，避免同一会话清单被逐文件并发写入");
    }

    [Fact]
    public void UploadControllers_ShouldPropagateRequestAbortedToFileOperations()
    {
        var documentsContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs".Replace('/', Path.DirectorySeparatorChar)));
        documentsContent.Should().Contain("HttpContext.RequestAborted",
            "控制器仍应把请求取消令牌透传给文档资源应用服务");
        documentsContent.Should().Contain("UploadFileAsync(",
            "上传接口应委派给独立应用服务，而不是重新内联文件处理逻辑");

        var documentFileAppServiceContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/DocumentFileAppService.cs".Replace('/', Path.DirectorySeparatorChar)));
        documentFileAppServiceContent.Should().Contain("await file.CopyToAsync(memoryStream, cancellationToken);",
            "应用服务应继续把请求取消令牌透传到文件拷贝");

        var documentFileAccessServiceContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/DocumentFileAccessService.cs".Replace('/', Path.DirectorySeparatorChar)));
        documentFileAccessServiceContent.Should().Contain("SaveUploadedExcelAsync(fileName, content, cancellationToken)",
            "共享文件访问组件应继续把取消令牌透传到 Excel 文件存储");
        documentFileAccessServiceContent.Should().Contain("SaveUploadedWordAsync(fileName, content, cancellationToken)",
            "共享文件访问组件应继续把取消令牌透传到 Word 文件存储");

        var compareContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Controllers/FileCompareController.cs".Replace('/', Path.DirectorySeparatorChar)));
        compareContent.Should().Contain("await file.CopyToAsync(memoryStream, cancellationToken);");
        compareContent.Should().Contain("SaveUploadedExcelAsync(existingFile.FileName, fileContent, cancellationToken)");
        compareContent.Should().Contain("SaveUploadedWordAsync(existingFile.FileName, fileContent, cancellationToken)");
        compareContent.Should().Contain("SaveUploadedExcelAsync(file.FileName, fileContent, cancellationToken)");
        compareContent.Should().Contain("SaveUploadedWordAsync(file.FileName, fileContent, cancellationToken)");
    }

    [Fact]
    public void MatchingEndpoints_ShouldBeSplitIntoFocusedControllers()
    {
        var controllerTypes = typeof(BaseApiController).Assembly
            .GetTypes()
            .Where(type =>
                !type.IsAbstract &&
                typeof(ControllerBase).IsAssignableFrom(type) &&
                type.Namespace == typeof(BaseApiController).Namespace &&
                type.Name.StartsWith("Matching", StringComparison.Ordinal))
            .ToList();

        controllerTypes.Select(type => type.Name).Should().Contain([
            "MatchingPreviewController",
            "MatchingExecutionController",
            "MatchingTaskController"
        ], "匹配预览、执行与下载应拆分为独立控制器；strict reuse 已从当前主链移除");

        controllerTypes.Should().NotContain(type => type.Name == "MatchingController",
            "巨型 MatchingController 应被拆分，避免继续堆叠职责");

        foreach (var controllerType in controllerTypes)
        {
            var constructor = controllerType
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .SingleOrDefault();

            constructor.Should().NotBeNull($"{controllerType.Name} 应保留单一公开构造函数，便于依赖管理");
            constructor!.GetParameters().Length.Should().BeLessThanOrEqualTo(3,
                $"{controllerType.Name} 应保持轻量依赖，避免再次退化为巨型控制器");
        }
    }

    [Fact]
    public void MatchingExecutionController_LlmStream_ShouldDeclareAuditOperationAttribute()
    {
        var method = typeof(MatchingExecutionController).GetMethod(nameof(MatchingExecutionController.LlmStream));

        method.Should().NotBeNull();
        var attribute = method!
            .GetCustomAttributes(typeof(AuditOperationAttribute), inherit: true)
            .OfType<AuditOperationAttribute>()
            .SingleOrDefault();

        attribute.Should().NotBeNull("SSE 流式填充入口也应进入控制器审计链路");
        attribute!.Operation.Should().Be("llm-stream");
        attribute.Resource.Should().Be("matching-fill");
    }

    [Fact]
    public void MatchingAndFileCompareControllers_ShouldDeclareAuthorizeAttributes()
    {
        typeof(MatchingApiControllerBase)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Should()
            .NotBeEmpty("匹配相关控制器应显式声明鉴权，避免仅依赖全局兜底策略");

        typeof(FileCompareController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Should()
            .NotBeEmpty("文件对比控制器应显式声明鉴权，避免后续调整中间件时意外裸露");
    }

    [Fact]
    public void MatchingFillTask_ShouldContainOwnershipMetadata()
    {
        var properties = typeof(AcceptanceSpecSystem.Data.Entities.MatchingFillTask)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        properties.Should().Contain("CreatedByUserId", "匹配任务需要记录创建用户，才能校验下载/复用归属");
        properties.Should().Contain("CompanyId", "匹配任务需要记录公司上下文，避免跨组织任务穿透");
    }

    [Fact]
    public void MatchingTaskAndReuseServices_ShouldCarryOwnershipAndPayloadVersion()
    {
        var workflowContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs".Replace('/', Path.DirectorySeparatorChar)));
        var snapshotContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/MatchingTaskSnapshotService.cs".Replace('/', Path.DirectorySeparatorChar)));
        var taskContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/MatchingTaskAppService.cs".Replace('/', Path.DirectorySeparatorChar)));
        workflowContent.Should().Contain("PayloadVersion", "任务快照应带版本元数据，便于未来兼容迁移");
        snapshotContent.Should().Contain("EnsureTaskOwnership", "任务快照服务应统一校验任务归属");
        taskContent.Should().Contain("DownloadAsync(ClaimsPrincipal user, string taskId)", "下载接口应结合当前用户校验任务归属");
        taskContent.Should().Contain("MatchingTaskSnapshotService", "下载应用服务应通过共享快照服务读取任务归属");
        File.Exists(Path.Combine(
                GetRepositoryRoot(),
                "src/AcceptanceSpecSystem.Api/Services/StrictReuseAppService.cs".Replace('/', Path.DirectorySeparatorChar)))
            .Should()
            .BeFalse("strict reuse 主链已经移除，不应再保留独立应用服务");
    }

    [Fact]
    public void PromptTemplate_ShouldNotRetainLegacyDefaultTemplateSemantics()
    {
        var repositoryContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Data/Repositories/PromptTemplateRepository.cs".Replace('/', Path.DirectorySeparatorChar)));
        var dtoContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/DTOs/PromptTemplateDtos.cs".Replace('/', Path.DirectorySeparatorChar)));

        repositoryContent.Should().NotContain("SetDefaultAsync", "Prompt 模板不应继续保留设默认模板仓储能力");
        dtoContent.Should().NotContain("IsDefault", "Prompt 模板 DTO 不应继续暴露默认模板语义");
    }

    [Fact]
    public void AiServiceSelection_ShouldDependOnInterface()
    {
        var files = new[]
        {
            "src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs",
            "src/AcceptanceSpecSystem.Api/Services/SpecSemanticSearchService.cs",
            "src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.cs",
            "src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelEmbeddingService.cs"
        };

        foreach (var relativePath in files)
        {
            var content = File.ReadAllText(Path.Combine(GetRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
            content.Should().NotContain("private readonly AiServiceSelector", $"{relativePath} 不应再以字段形式依赖具体类");
            content.Should().NotContain("(AiServiceSelector ", $"{relativePath} 不应再以构造函数参数形式依赖具体类");
            content.Should().Contain("IAiServiceSelector", $"{relativePath} 应显式注入 IAiServiceSelector");
        }
    }

    [Fact]
    public void SemanticKernelFactory_ShouldAvoidHardcodedAzurePreviewVersion_AndSyncBlockingDispose()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Core/AI/SemanticKernel/SemanticKernelServiceFactory.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("\"2024-02-15-preview\"", "Azure OpenAI API 版本应配置化，而不是硬编码在代码里");
        content.Should().NotContain("GetAwaiter().GetResult()", "异步资源释放不应通过同步阻塞完成");
    }

    [Fact]
    public void OllamaNativeChatCompletionService_ShouldNotMutateInjectedHttpClientTimeout()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Core/AI/SemanticKernel/OllamaNativeChatCompletionService.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("_httpClient.Timeout =", "Ollama 原生服务不应改写注入 HttpClient 的共享状态");
    }

    [Fact]
    public void Program_ShouldConfigureOllamaNativeHttpClientLongTimeout()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Program.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("AddHttpClient(AiServiceHttpClientDefaults.OllamaNativeChatClientName", "Ollama 原生聊天不能使用 HttpClient 默认 100 秒超时");
        content.Should().Contain("client.Timeout = AiServiceHttpClientDefaults.LongRunningNetworkTimeout", "慢模型推理应沿用长网络超时配置");
    }

    [Fact]
    public void AuthRolePermissionTouch_ShouldUseSetBasedUpdate()
    {
        var authRoleAppServiceContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/AuthRoleAppService.cs".Replace('/', Path.DirectorySeparatorChar)));
        authRoleAppServiceContent.Should().Contain("ExecuteUpdateAsync", "角色变更触达用户权限版本应使用集合更新，而不是先拉全量用户到内存");

        var seedContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/AuthUserSeedService.cs".Replace('/', Path.DirectorySeparatorChar)));
        seedContent.Should().Contain("ExecuteUpdateAsync", "初始化角色修正权限版本时也应使用集合更新");
        seedContent.Should().Contain("BeginTransactionAsync", "根组织路径初始化应通过事务保证原子性");
    }

    [Fact]
    public void LoginAndSmartFillViews_ShouldContainReviewFixes()
    {
        var loginContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/login/index.vue".Replace('/', Path.DirectorySeparatorChar)));
        loginContent.Should().Contain("const topMenu = getTopMenu(true);", "登录页应先保存菜单对象，再校验是否可跳转");
        loginContent.Should().Contain("if (!topMenu?.path)", "登录页应在无菜单时给出明确提示，而不是抛空引用");

        var smartFillContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/index.vue".Replace('/', Path.DirectorySeparatorChar)));
        smartFillContent.Should().Contain("previewAbortController", "批量预览应保留 AbortController 取消陈旧请求");
        smartFillContent.Should().Contain("signal: controller.signal", "批量预览请求应显式传递取消信号");
        smartFillContent.Should().Contain("document.body.appendChild(a);", "Object URL 下载前应把锚点挂到 DOM，兼容 Firefox/Safari");
    }

    [Fact]
    public void SessionExpiryHandling_ShouldPreserveRedirectPath_AndRequireExplicitRelogin()
    {
        var httpContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/utils/http/index.ts".Replace('/', Path.DirectorySeparatorChar)));
        httpContent.Should().Contain("ElMessageBox.alert", "登录态过期时应以确认弹框提示用户，而不是只闪过一条消息后立即跳转");
        httpContent.Should().Contain("useUserStoreHook().logOut(currentPath)", "会话失效后应携带当前页面地址，便于重新登录后回跳");

        var userStoreContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/store/modules/user.ts".Replace('/', Path.DirectorySeparatorChar)));
        userStoreContent.Should().Contain("logOut(redirectPath?: string)", "登出逻辑应支持接收回跳地址");
        userStoreContent.Should().Contain("query: { redirect: redirectPath }", "跳登录页时应保留会话失效前的页面地址");

        var loginContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/login/index.vue".Replace('/', Path.DirectorySeparatorChar)));
        loginContent.Should().Contain("useRoute", "登录页应读取 redirect 查询参数，支持重新登录后回跳");
        loginContent.Should().Contain("route.query.redirect", "登录成功后应优先跳回会话失效前的页面");
    }

    [Fact]
    public void SmartFillMatchConfig_ShouldNotExposeRemovedLlmEntityResolutionOptions()
    {
        var apiContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/api/matching.ts".Replace('/', Path.DirectorySeparatorChar)));
        apiContent.Should().NotContain("useLlmEntityResolution");
        apiContent.Should().NotContain("llmEntityResolutionTopCandidates");
        apiContent.Should().NotContain("llmEntityPositiveConfidenceThreshold");
        apiContent.Should().NotContain("llmEntityConflictReviewConfidenceThreshold");
        apiContent.Should().NotContain("llmEntityConflictRejectConfidenceThreshold");

        var matchConfigContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/MatchConfig.vue".Replace('/', Path.DirectorySeparatorChar)));
        matchConfigContent.Should().NotContain("config.useLlmEntityResolution");
        matchConfigContent.Should().NotContain("config.llmEntityResolutionTopCandidates");
        matchConfigContent.Should().NotContain("config.llmEntityPositiveConfidenceThreshold");
        matchConfigContent.Should().NotContain("config.llmEntityConflictReviewConfidenceThreshold");
        matchConfigContent.Should().NotContain("config.llmEntityConflictRejectConfidenceThreshold");
        matchConfigContent.Should().NotContain("LLM 实体判别");
    }

    [Fact]
    public void MatchingStrategyLegacyCode_ShouldBeRemovedFromFrontendAndBackend()
    {
        var apiContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/api/matching.ts".Replace('/', Path.DirectorySeparatorChar)));
        apiContent.Should().NotContain("export enum MatchingStrategy");
        apiContent.Should().NotContain("matchingStrategy?:");
        apiContent.Should().NotContain("matchingStrategy:");

        var matchConfigContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/MatchConfig.vue".Replace('/', Path.DirectorySeparatorChar)));
        matchConfigContent.Should().Contain("证据裁决");
        matchConfigContent.Should().NotContain("MatchingStrategy");
        matchConfigContent.Should().NotContain("isMultiStage");
        matchConfigContent.Should().NotContain("单阶段");
        matchConfigContent.Should().NotContain("多阶段");

        var aiServiceConfigContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/config/ai-services/index.vue".Replace('/', Path.DirectorySeparatorChar)));
        aiServiceConfigContent.Should().Contain("证据裁决");
        aiServiceConfigContent.Should().NotContain("defaultMatchingStrategy");
        aiServiceConfigContent.Should().NotContain("单阶段");
        aiServiceConfigContent.Should().NotContain("多阶段");

        var apiDtoContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/DTOs/AiServiceDtos.cs".Replace('/', Path.DirectorySeparatorChar)));
        apiDtoContent.Should().NotContain("DefaultMatchingStrategy");

        var matchingDtoContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs".Replace('/', Path.DirectorySeparatorChar)));
        matchingDtoContent.Should().NotContain("MatchingStrategy");

        var matchingServiceContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.cs".Replace('/', Path.DirectorySeparatorChar)));
        matchingServiceContent.Should().NotContain("SelectBestBySingleStage");
        matchingServiceContent.Should().NotContain("SelectBestByMultiStage");
        matchingServiceContent.Should().NotContain("MatchingStrategy.MultiStage");

        var dataEntityContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Data/Entities/AiServiceConfig.cs".Replace('/', Path.DirectorySeparatorChar)));
        dataEntityContent.Should().NotContain("DefaultMatchingStrategy");

        var dataEnumContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Data/Entities/Enums.cs".Replace('/', Path.DirectorySeparatorChar)));
        dataEnumContent.Should().NotContain("AiServiceDefaultMatchingStrategy");
    }

    [Fact]
    public void PromptTemplateSceneMappings_ShouldNotIncludeRemovedMatchingEntityResolution()
    {
        var dataEntityContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Data/Entities/PromptTemplate.cs".Replace('/', Path.DirectorySeparatorChar)));
        dataEntityContent.Should().NotContain("MatchingEntityResolution");

        var providerContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Data/Providers/CoreProviderAdapters.cs".Replace('/', Path.DirectorySeparatorChar)));
        providerContent.Should().NotContain("PromptTemplateScene.MatchingEntityResolution");
        providerContent.Should().NotContain("Entities.PromptTemplateScene.MatchingEntityResolution");

        var controllerContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Controllers/PromptTemplatesController.cs".Replace('/', Path.DirectorySeparatorChar)));
        controllerContent.Should().NotContain("CorePromptTemplateScene.MatchingEntityResolution");
        controllerContent.Should().NotContain("PromptTemplateScene.MatchingEntityResolution");
    }

    [Fact]
    public void ScoreDetailDialog_ShouldHideRemovedEntityEvidenceSection()
    {
        var bestMatchSectionContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailBestMatchSection.vue".Replace('/', Path.DirectorySeparatorChar)));
        var candidateListContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailCandidateList.vue".Replace('/', Path.DirectorySeparatorChar)));

        bestMatchSectionContent.Should().NotContain("实体证据");
        bestMatchSectionContent.Should().NotContain("bestMatchEntities");
        candidateListContent.Should().NotContain("candidate.entities?.length");
    }

    [Fact]
    public void ScoreDetailBestMatchSection_ShouldNotContainLegacyKeywordFallbackExplanation()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/ScoreDetailBestMatchSection.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("KeywordOverlap");
        content.Should().NotContain("关键词项");
        content.Should().NotContain("有效关键词 token");
    }

    [Fact]
    public void Program_ShouldNotFallbackCorsToAllowAnyOrigin()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Program.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("AllowAnyOrigin()", "CORS 来源缺失时不应退化为全开放");
        content.Should().Contain("Cors:AllowedOrigins", "启动期应对 CORS 来源配置做显式校验");
    }

    [Fact]
    public void ProductionConfig_ShouldUseExplicitCorsOrigins()
    {
        var content = ReadFileText("src/AcceptanceSpecSystem.Api/appsettings.Production.json");

        content.Should().NotContain("\"AllowedOrigins\": [ \"*\" ]",
            "Production 配置必须给出显式 CORS 白名单，不能与启动期校验相冲突");
    }

    [Fact]
    public void WebBuildScript_ShouldRunTypecheckBeforeBundling()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/package.json".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("\"build\": \"pnpm typecheck &&",
            "前端 build 应先执行 typecheck，避免类型错误被 vite 构建掩盖");
    }

    [Fact]
    public void WebPackage_ShouldPinBaselineBrowserMappingToAvoidOutdatedBuildWarning()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/package.json".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("\"baseline-browser-mapping\"",
            "前端依赖应显式提升 baseline-browser-mapping，避免构建时反复提示数据过旧");
    }

    [Fact]
    public void UseNav_ShouldUsePublicPathForLogoSvg()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/layout/hooks/useNav.ts".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("new URL(\"/SAA.svg\", import.meta.url).href",
            "public 目录资源应直接走公开路径，避免触发 vite-svg-loader 的模块加载分支");
        content.Should().Contain("return \"/SAA.svg\";",
            "导航 Logo 应直接返回 public 目录下的静态路径");
    }

    [Fact]
    public void LoginView_ShouldBindPublicPathForLogoSvg()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/login/index.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("src=\"/SAA.svg\"",
            "登录页 Logo 不应使用静态模板资源路径，避免触发 Vite 资源转换和 svg loader");
        content.Should().Contain("const loginLogoUrl = \"/SAA.svg\";",
            "登录页应通过脚本常量显式使用 public 目录路径");
        content.Should().Contain(":src=\"loginLogoUrl\"",
            "登录页 Logo 应通过绑定形式输出 public 资源路径");
    }

    [Fact]
    public void WordFile_ShouldContainOwnershipMetadata_AndDocumentsController_ShouldApplyWordFileScope()
    {
        var propertyNames = typeof(AcceptanceSpecSystem.Data.Entities.WordFile)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        propertyNames.Should().Contain("CompanyId");
        propertyNames.Should().Contain("CreatedByUserId");
        propertyNames.Should().Contain("OwnerOrgUnitId");

        var documentsContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs".Replace('/', Path.DirectorySeparatorChar)));
        documentsContent.Should().Contain("DocumentFileAppService",
            "文档控制器应通过独立应用服务执行文件列表与单文件访问编排");

        var documentFileAccessContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/DocumentFileAccessService.cs".Replace('/', Path.DirectorySeparatorChar)));
        documentFileAccessContent.Should().Contain("WordFileDataScopeHelper",
            "文件级范围校验应下沉到共享文件访问组件");
        documentFileAccessContent.Should().Contain("GetAccessibleWordFileAsync",
            "共享文件访问组件应统一提供归属校验后的单文件读取入口");
    }

    [Fact]
    public void SmartFillViews_ShouldUseMatchingFillPermissionNames_AndPreserveManualConfirmation()
    {
        var smartFillContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/index.vue".Replace('/', Path.DirectorySeparatorChar)));
        smartFillContent.Should().Contain("btn:matching-fill:llm-stream",
            "LLM 流式复核按钮权限应与后端 matching-fill 资源保持一致");
        smartFillContent.Should().NotContain("btn:matching:llm-stream",
            "旧的 matching:llm-stream 权限命名应被移除");

        var tabsContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/BatchPreviewTabs.vue".Replace('/', Path.DirectorySeparatorChar)));
        tabsContent.Should().Contain("manualConfirmed",
            "批量预览页应把人工确认标记透传到执行请求，避免类型漂移和行为回退");
    }

    [Fact]
    public void PromptTemplateView_ShouldUseGranularPreviewAndResetPermissions()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/config/prompt-templates/index.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("btn:prompt-template:preview",
            "模板预览应使用独立的 preview 权限");
        content.Should().Contain("btn:prompt-template:reset-system",
            "恢复默认应使用独立的 reset-system 权限");
    }

    [Fact]
    public void LegacyConfigRedirects_ShouldNotHangUnderConfigMenuRoute()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/router/modules/config.ts".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("AuthRolesConfigLegacy",
            "旧 /config/* 兼容跳转不应继续挂在 Config 父路由下，否则会先被 menu:config 拦住");
        content.Should().NotContain("SystemUsersConfigLegacy",
            "旧 /config/* 兼容跳转不应继续挂在 Config 父路由下，否则会先被 menu:config 拦住");
        content.Should().NotContain("OrgUnitsConfigLegacy",
            "旧 /config/* 兼容跳转不应继续挂在 Config 父路由下，否则会先被 menu:config 拦住");
    }

    [Fact]
    public void AuthRolePage_ShouldNotExposeMultiOrgScopePicker()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/config/auth-roles/index.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("needsMultiOrg", "单组织契约下角色页不应再暴露多节点范围选择分支");
        content.Should().NotContain("scopeType === 3", "角色页不应继续兼容自定义多节点范围类型");
    }

    [Fact]
    public void AuthRolePage_ShouldRenderBuiltInRoleAsReadonlyInEditDialog()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/config/auth-roles/index.vue".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("内置角色只读，不可保存",
            "内置角色允许打开编辑弹窗时，应明确提示当前为只读模式");
        content.Should().Contain(":disabled=\"editForm.isBuiltIn\"",
            "编辑弹窗中的输入控件应随内置角色状态进入只读态");
        content.Should().Contain("{{ editForm.isBuiltIn ? \"不可保存\" : \"保存\" }}",
            "内置角色弹窗底部按钮文案应明确告知不可保存");
    }

    [Fact]
    public void BatchReplyPage_ShouldRenderDuplicateResolutionDialog()
    {
        var pageContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/batch-reply/index.vue".Replace('/', Path.DirectorySeparatorChar)));
        var panelContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/components/BatchTableConfig.vue".Replace('/', Path.DirectorySeparatorChar)));

        pageContent.Should().Contain("重复项处理",
            "批量回复在遇到重复项目/规格时应弹出明确的处理对话框");
        pageContent.Should().Contain("保留首条",
            "冲突处理对话框应允许用户选择保留首条");
        pageContent.Should().Contain("保留末条",
            "冲突处理对话框应允许用户选择保留末条");
        pageContent.Should().Contain("跳过该组",
            "冲突处理对话框应允许用户选择跳过该组");
        pageContent.Should().Contain("duplicateGroups",
            "页面需要读取后端返回的结构化重复冲突，而不是只靠通用错误文案");
        panelContent.Should().Contain("当前 Sheet/表格仍有问题待处理",
            "当前表格区域仍应保留就地反馈，与弹窗处理形成闭环");
    }

    [Fact]
    public void HttpRequestInterceptor_ShouldPreserveAuditHeaders_WhenBeforeRequestCallbackExists()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/utils/http/index.ts".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("ensureAuditHeaders(config);", "请求进入自定义 beforeRequestCallback 分支前后都应补齐审计头");
        content.Should().Contain("return PureHttp.ensureAuthorization(config);", "beforeRequestCallback 分支存在时也应补齐鉴权头，避免绕过 Authorization 注入");
        content.Should().Contain("await callback(config);", "自定义回调仍应保留对请求配置的扩展能力");
        content.Should().Contain("return PureHttp.applyBeforeRequestCallback(config, beforeCallback);", "命中 beforeRequestCallback 时不应提前返回未鉴权配置");

        var callbackIndex = content.IndexOf("return PureHttp.applyBeforeRequestCallback(config, beforeCallback);", StringComparison.Ordinal);
        var headerIndex = content.IndexOf("ensureAuditHeaders(config);", StringComparison.Ordinal);
        headerIndex.Should().BeGreaterThanOrEqualTo(0);
        callbackIndex.Should().BeGreaterThan(0);
        headerIndex.Should().BeLessThan(callbackIndex, "应先补齐审计头，再交给 beforeRequestCallback 自定义处理");

        var helperIndex = content.IndexOf("private static async applyBeforeRequestCallback", StringComparison.Ordinal);
        helperIndex.Should().BeGreaterThan(0);
        var helperContent = content.Substring(helperIndex, Math.Min(400, content.Length - helperIndex));
        helperContent.Should().Contain("await callback(config);", "beforeRequestCallback 帮助方法应先执行自定义回调");
        helperContent.Should().Contain("ensureAuditHeaders(config);", "自定义回调之后仍应重新补齐审计头");
        helperContent.Should().Contain("return PureHttp.ensureAuthorization(config);", "自定义回调之后仍应继续执行统一的鉴权补头");
    }

    [Fact]
    public void EmbeddingCacheRepository_DeleteMethods_ShouldUseExecuteDeleteAsync()
    {
        var content = ReadFileText("src/AcceptanceSpecSystem.Data/Repositories/EmbeddingCacheRepository.cs");

        content.Should().Contain("Where(e => e.ModelName == modelName)\n            .ExecuteDeleteAsync()", "按模型名批量删除应直接下推到数据库");
        content.Should().Contain("Where(e => e.ExpiresAt != null && e.ExpiresAt < beforeTime)\n            .ExecuteDeleteAsync()", "过期缓存清理应直接下推到数据库");
        content.Should().Contain("Where(e => e.ModelName == modelName && e.ModelVersion != modelVersion)\n            .ExecuteDeleteAsync()", "按模型版本批量失效应直接下推到数据库");
    }

    [Fact]
    public void PromptTemplateProvider_ShouldAvoidDirectAppDbContextDependency()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Data/Providers/CoreProviderAdapters.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("private readonly AppDbContext", "PromptTemplateProvider 不应再直接依赖具体 DbContext");
        content.Should().Contain("IUnitOfWork", "PromptTemplateProvider 至少应通过 UoW 抽象提交变更");
    }

    [Fact]
    public void AuthAccessService_ShouldAvoidDirectAppDbContextDependency()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/AuthAccessService.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("private readonly AppDbContext", "鉴权访问服务不应再直接持有 AppDbContext");
        content.Should().Contain("ISystemUserRepository", "用户访问应通过专用仓储抽象完成");
    }

    [Fact]
    public void AuthDataScopeService_ShouldUseMemoryCache()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/AuthDataScopeService.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("IMemoryCache", "数据范围服务应通过内存缓存复用组织树计算结果");
        content.Should().Contain("_memoryCache", "数据范围服务应持有缓存实例");
    }

    [Fact]
    public void LegacyTextProcessingRepositories_ShouldBeRemoved()
    {
        var repositoryRoot = GetRepositoryRoot();
        var legacyFiles = new[]
        {
            "src/AcceptanceSpecSystem.Data/Repositories/KeywordRepository.cs",
            "src/AcceptanceSpecSystem.Data/Repositories/IKeywordRepository.cs",
            "src/AcceptanceSpecSystem.Data/Repositories/TextProcessingConfigRepository.cs",
            "src/AcceptanceSpecSystem.Data/Repositories/ITextProcessingConfigRepository.cs",
            "src/AcceptanceSpecSystem.Data/Repositories/SynonymRepository.cs",
            "src/AcceptanceSpecSystem.Data/Repositories/ISynonymRepository.cs"
        };

        foreach (var relativePath in legacyFiles)
        {
            File.Exists(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Should().BeFalse($"{relativePath} 应随旧文本预处理体系一起移除");
        }
    }

    [Fact]
    public void MatchingTaskSnapshotService_ShouldRejectTasksWithoutOwnershipMetadata()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/MatchingTaskSnapshotService.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("!entity.CreatedByUserId.HasValue", "缺少用户归属的任务应被显式拒绝");
        content.Should().Contain("!entity.CompanyId.HasValue", "缺少公司归属的任务应被显式拒绝");
    }

    [Fact]
    public void MatchingSimilarityPermissionResidue_ShouldBeRemovedFromConventionsAndSeedWhitelist()
    {
        var permissionConventionContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Authorization/PermissionConventions.cs".Replace('/', Path.DirectorySeparatorChar)));
        var authSeedContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/AuthUserSeedService.cs".Replace('/', Path.DirectorySeparatorChar)));

        permissionConventionContent.Should().NotContain("return \"similarity\"",
            "legacy /api/matching/similarity 已移除，不应继续保留专用动作映射");
        authSeedContent.Should().NotContain("\"api:matching:similarity\"",
            "普通角色白名单不应继续残留 similarity API 权限");
    }

    [Fact]
    public void AppDbContext_ShouldLogWhenApiKeyDecryptFallbackIsTriggered()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("Trace.TraceWarning", "ApiKey 解密兼容路径至少应输出告警，避免静默吞错");
    }

    [Fact]
    public void MatchingApiControllerBase_ShouldDocumentExceptionBoundary()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Controllers/MatchingApiControllerBase.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("其余异常继续交给全局异常中间件处理", "基类应明确说明异常边界，避免控制器和中间件职责含混");
    }

    [Fact]
    public void MatchingTaskController_Download_ShouldConstrainTaskIdFormat()
    {
        var method = typeof(MatchingTaskController).GetMethod(nameof(MatchingTaskController.Download));

        method.Should().NotBeNull();
        var attribute = method!
            .GetCustomAttributes(typeof(HttpGetAttribute), inherit: true)
            .OfType<HttpGetAttribute>()
            .SingleOrDefault();

        attribute.Should().NotBeNull("任务下载接口应限制 taskId 路由格式");
        attribute!.Template.Should().Contain("{taskId:regex(^[[a-f0-9]]{{32}}$)}");
    }

    [Fact]
    public void SemanticKernelFactory_ShouldAvoidNullForgivingForRequiredModels()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Core/AI/SemanticKernel/SemanticKernelServiceFactory.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().NotContain("config.LlmModel!", "必填模型应通过显式 Guard 获取，而不是 null-forgiving");
        content.Should().NotContain("config.EmbeddingModel!", "必填模型应通过显式 Guard 获取，而不是 null-forgiving");
    }

    [Fact]
    public void StrictReuseDialog_ShouldBeRemoved()
    {
        File.Exists(Path.Combine(
                GetRepositoryRoot(),
                "web/src/views/smart-fill/components/StrictReuseDialog.vue".Replace('/', Path.DirectorySeparatorChar)))
            .Should()
            .BeFalse("前端 strict reuse 入口已经移除，不应继续保留对话框组件");
    }

    [Fact]
    public void UserStore_LogOut_ShouldClearPermissionAndRouteCaches()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/store/modules/user.ts".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("usePermissionStoreHook().clearAllCachePage()", "登出时应清掉权限菜单和 keepAlive 缓存");
        content.Should().Contain("resetRouter()", "登出时应重置静态路由与菜单状态");
        content.Should().NotContain("\"async-routes\"", "静态路由启动后不应再维护 async-routes 缓存");
    }

    [Fact]
    public void SmartFill_OnUnmount_ShouldAbortPreviewRequestsToo()
    {
        var smartFillContent = ReadFileText("web/src/views/smart-fill/index.vue");
        smartFillContent.Should().Contain(
            "onBeforeUnmount(() => {\n  invalidatePendingPreview();\n  stopLlmStream();\n});",
            "页面卸载时应同时取消未完成的批量预览请求和流式请求，避免离页后仍占用后端算力");
    }

    [Fact]
    public void AiServicesController_ShouldReuseSemanticKernelAzureApiVersion()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Controllers/AiServicesController.cs".Replace('/', Path.DirectorySeparatorChar)));

        content.Should().Contain("IOptions<SemanticKernelOptions>", "Azure OpenAI 探测接口应复用统一的 SemanticKernel 配置来源");
        content.Should().Contain("_azureOpenAiApiVersion", "控制器应缓存统一的 Azure OpenAI API 版本配置");
        content.Should().NotContain("\"2024-02-15-preview\"", "模型探测接口不应再硬编码 preview API version");
    }

    [Fact]
    public void SmartFillLlmStream_ShouldUseSharedAuthorizedFetchHelper()
    {
        var httpContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/utils/http/index.ts".Replace('/', Path.DirectorySeparatorChar)));
        httpContent.Should().Contain("export async function createAuthorizedFetchInit",
            "HTTP 工具层应提供可复用的原生 fetch 鉴权封装，供 SSE/流式接口复用");
        httpContent.Should().Contain("export async function authorizedFetch",
            "HTTP 工具层应提供直接可复用的原生 fetch 帮助方法，减少页面侧重复拼装逻辑");
        httpContent.Should().Contain("PureHttp.handleAuthFailure",
            "原生 fetch 鉴权封装应复用统一的 401/403 处理逻辑");

        var matchingApiContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/api/matching.ts".Replace('/', Path.DirectorySeparatorChar)));
        matchingApiContent.Should().Contain("export const requestMatchLlmStream = async",
            "智能填充流式请求应收敛到匹配 API 模块，避免页面直接拼装原生请求");
        matchingApiContent.Should().Contain("createAuthorizedFetchInit(url, {",
            "匹配 API 模块应复用共享鉴权初始化");
        matchingApiContent.Should().Contain("ensureFetchResponseAuthHandled(response, url)",
            "匹配 API 模块应复用统一的认证失败处理");

        var smartFillContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "web/src/views/smart-fill/index.vue".Replace('/', Path.DirectorySeparatorChar)));
        smartFillContent.Should().Contain("requestMatchLlmStream(payload, controller.signal)",
            "Smart Fill 页面应通过类型化匹配 API 发起流式请求，而不是直接发原生 fetch");
        smartFillContent.Should().Contain("createMatchLlmStreamRequest({",
            "Smart Fill 页面应先构造类型化 payload，再交给匹配 API 发送");
        smartFillContent.Should().NotContain("authorizedFetch(\"/api/matching/llm-stream\"",
            "页面层不应再直接调用原生鉴权 fetch 访问 llm-stream");
        smartFillContent.Should().NotContain("Authorization: formatToken(",
            "Smart Fill 不应再手工拼接流式请求的 Authorization 头");
    }

    [Fact]
    public void SmartFillUpload_ShouldExposeDedicatedTableMetadataLoadingState()
    {
        var smartFillContent = ReadFileText("web/src/views/smart-fill/index.vue");
        smartFillContent.Should().Contain("const loadingUploadedFileTables = ref(false);",
            "智能填充上传后应单独跟踪表格结构读取状态，而不是把上传与解析混成一个阶段");
        smartFillContent.Should().Contain("正在读取表格结构，请稍候",
            "智能填充页应明确提示当前仍在读取表格结构，避免用户误以为页面卡住");
        smartFillContent.Should().Contain("!loadingUploadedFileTables.value",
            "上传后的表格结构尚未读取完成前，不应允许直接进入下一步");

        var uploadContent = ReadFileText("web/src/views/data-import/components/FileUpload.vue");
        uploadContent.Should().Contain("tableCountReady === false",
            "共享上传组件应识别表格数量仍在后台读取的状态");
        uploadContent.Should().Contain("正在读取",
            "共享上传组件应在表格元信息尚未就绪时提供明确文案，而不是直接显示 0 个表格");
    }

    [Fact]
    public void MatchingServices_ShouldGuardCandidateVolume_AndBatchEmbeddingHydration()
    {
        var previewContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/MatchingPreviewAppService.cs".Replace('/', Path.DirectorySeparatorChar)));
        var workflowContent = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs".Replace('/', Path.DirectorySeparatorChar)));

        foreach (var content in new[] { previewContent, workflowContent })
        {
            content.Should().Contain("MaxScopedCandidateCount",
                "匹配服务应在加载候选前限制候选范围大小，避免单请求全量拉入内存");
            content.Should().Contain("EnsureCandidateScopeWithinLimit",
                "匹配服务应对候选总量做显式保护并给出可操作的错误提示");
            content.Should().Contain("GenerateEmbeddingsInBatchesAsync",
                "Embedding 缺失候选应分批生成，避免单次远程调用承载全部候选");
        }
    }

    private static string[] ReadFile(string relativePath)
    {
        var repositoryRoot = GetRepositoryRoot();
        return File.ReadAllLines(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string ReadFileText(string relativePath)
    {
        var repositoryRoot = GetRepositoryRoot();
        return File.ReadAllText(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n");
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
}
