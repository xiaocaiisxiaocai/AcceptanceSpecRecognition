using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class FrontendViewBoundaryRefactorTests
{
    [Fact]
    public void ScoreDetailDialog_ShouldBeSplitIntoShell_Sections_AndComposable()
    {
        var repositoryRoot = GetRepositoryRoot();
        var dialogPath = Path.Combine(
            repositoryRoot,
            "web/src/views/smart-fill/components/ScoreDetailDialog.vue".Replace('/', Path.DirectorySeparatorChar));
        var formattersPath = Path.Combine(
            repositoryRoot,
            "web/src/views/smart-fill/components/scoreDetail.formatters.ts".Replace('/', Path.DirectorySeparatorChar));
        var composablePath = Path.Combine(
            repositoryRoot,
            "web/src/views/smart-fill/composables/useScoreDetailDiff.ts".Replace('/', Path.DirectorySeparatorChar));
        var bestMatchSectionPath = Path.Combine(
            repositoryRoot,
            "web/src/views/smart-fill/components/ScoreDetailBestMatchSection.vue".Replace('/', Path.DirectorySeparatorChar));
        var diffSectionPath = Path.Combine(
            repositoryRoot,
            "web/src/views/smart-fill/components/ScoreDetailDiffSection.vue".Replace('/', Path.DirectorySeparatorChar));
        var candidateListPath = Path.Combine(
            repositoryRoot,
            "web/src/views/smart-fill/components/ScoreDetailCandidateList.vue".Replace('/', Path.DirectorySeparatorChar));

        File.Exists(formattersPath).Should().BeTrue("匹配详情弹窗的格式化函数应迁移到独立 helper 文件");
        File.Exists(composablePath).Should().BeTrue("匹配详情弹窗的 diff 派生逻辑应迁移到独立 composable 文件");
        File.Exists(bestMatchSectionPath).Should().BeTrue("匹配详情弹窗应拆出最佳匹配展示区块");
        File.Exists(diffSectionPath).Should().BeTrue("匹配详情弹窗应拆出差异对照区块");
        File.Exists(candidateListPath).Should().BeTrue("匹配详情弹窗应拆出候选列表区块");

        var dialogContent = File.ReadAllText(dialogPath);
        dialogContent.Should().Contain("ScoreDetailBestMatchSection",
            "弹窗壳应显式组合最佳匹配区块组件");
        dialogContent.Should().Contain("ScoreDetailDiffSection",
            "弹窗壳应显式组合差异对照区块组件");
        dialogContent.Should().Contain("ScoreDetailCandidateList",
            "弹窗壳应显式组合候选列表组件");
        dialogContent.Should().Contain("useScoreDetailDiff",
            "弹窗壳应通过 composable 统一收敛 diff 和候选比较状态");
    }

    [Fact]
    public void DataImportPage_ShouldBeSplitIntoShell_Steps_AndComposableBoundaries()
    {
        var repositoryRoot = GetRepositoryRoot();
        var indexPath = Path.Combine(
            repositoryRoot,
            "web/src/views/data-import/index.vue".Replace('/', Path.DirectorySeparatorChar));
        var helperPath = Path.Combine(
            repositoryRoot,
            "web/src/views/data-import/dataImport.helpers.ts".Replace('/', Path.DirectorySeparatorChar));
        var typesPath = Path.Combine(
            repositoryRoot,
            "web/src/views/data-import/dataImport.types.ts".Replace('/', Path.DirectorySeparatorChar));
        var mappingComposablePath = Path.Combine(
            repositoryRoot,
            "web/src/views/data-import/composables/useDataImportMapping.ts".Replace('/', Path.DirectorySeparatorChar));
        var previewSelectionComposablePath = Path.Combine(
            repositoryRoot,
            "web/src/views/data-import/composables/useDataImportPreviewSelection.ts".Replace('/', Path.DirectorySeparatorChar));
        var executionComposablePath = Path.Combine(
            repositoryRoot,
            "web/src/views/data-import/composables/useDataImportExecution.ts".Replace('/', Path.DirectorySeparatorChar));

        foreach (var relativePath in new[]
                 {
                     "web/src/views/data-import/components/DataImportStepUpload.vue",
                     "web/src/views/data-import/components/DataImportStepTableSelect.vue",
                     "web/src/views/data-import/components/DataImportStepMapping.vue",
                     "web/src/views/data-import/components/DataImportStepTarget.vue",
                     "web/src/views/data-import/components/DataImportStepConfirm.vue",
                     "web/src/views/data-import/components/DataImportDifferenceDialog.vue"
                 })
        {
            File.Exists(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Should()
                .BeTrue($"{relativePath} 应作为数据导入页面拆分后的聚焦组件存在");
        }

        File.Exists(helperPath).Should().BeTrue("数据导入页应抽离共享 helper");
        File.Exists(typesPath).Should().BeTrue("数据导入页应抽离共享类型");
        File.Exists(mappingComposablePath).Should().BeTrue("数据导入页应抽离映射管理 composable");
        File.Exists(previewSelectionComposablePath).Should().BeTrue("数据导入页应抽离待导入预览选择 composable");
        File.Exists(executionComposablePath).Should().BeTrue("数据导入页应抽离导入执行 composable");

        var indexContent = File.ReadAllText(indexPath);
        indexContent.Should().Contain("DataImportStepUpload",
            "页面壳应通过步骤组件承载上传区，而不是继续把模板堆在主文件里");
        indexContent.Should().Contain("DataImportStepTableSelect",
            "页面壳应通过步骤组件承载表格选择区");
        indexContent.Should().Contain("DataImportStepMapping",
            "页面壳应通过步骤组件承载映射配置区");
        indexContent.Should().Contain("DataImportStepTarget",
            "页面壳应通过步骤组件承载目标选择区");
        indexContent.Should().Contain("DataImportStepConfirm",
            "页面壳应通过步骤组件承载确认导入区");
        indexContent.Should().Contain("DataImportDifferenceDialog",
            "页面壳应通过独立弹窗组件承载差异确认区");
        indexContent.Should().Contain("useDataImportMapping",
            "页面壳应通过 composable 收敛映射相关逻辑");
        indexContent.Should().Contain("useDataImportPreviewSelection",
            "页面壳应通过 composable 收敛待导入预览选择逻辑");
        indexContent.Should().Contain("useDataImportExecution",
            "页面壳应通过 composable 收敛导入执行和结果聚合逻辑");
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
