using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class ExecutionHistoryFrontendRegressionTests
{
    [Fact]
    public void NavigationManifest_ShouldExposeExecutionHistoryPage()
    {
        var content = ReadRepositoryFile("shared/navigation/navigation-manifest.json");

        content.Should().Contain("\"id\": \"other-execution-history\"");
        content.Should().Contain("\"code\": \"page:other:execution-history\"");
        content.Should().Contain("\"title\": \"执行记录\"");
        content.Should().Contain("\"path\": \"/other/execution-history\"");
    }

    [Fact]
    public void OtherRoute_And_Pages_ShouldWireExecutionHistoryViews()
    {
        var routeContent = ReadRepositoryFile("web/src/router/modules/other.ts");
        routeContent.Should().Contain("path: \"/other/execution-history\"");
        routeContent.Should().Contain("name: \"ExecutionHistory\"");
        routeContent.Should().Contain("component: () => import(\"@/views/other/execution-history/index.vue\")");
        routeContent.Should().Contain("permissions: getPagePermission(\"other-execution-history\")");

        var apiContent = ReadRepositoryFile("web/src/api/execution-history.ts");
        apiContent.Should().Contain("getExecutionHistoryList");
        apiContent.Should().Contain("getExecutionHistoryDetail");
        apiContent.Should().Contain("/api/execution-history");

        var pageContent = ReadRepositoryFile("web/src/views/other/execution-history/index.vue");
        pageContent.Should().Contain("执行记录");
        pageContent.Should().Contain("任务下拉");
        pageContent.Should().Contain("ExecutionHistorySmartFillPlayback");
        pageContent.Should().Contain("ExecutionHistoryBatchReplyDetail");
        pageContent.Should().Contain("完全匹配");
        pageContent.Should().Contain("未采用/未匹配");
        pageContent.Should().Contain("getExecutionHistoryList");
        pageContent.Should().Contain("getExecutionHistoryDetail");

        var smartFillPlaybackContent = ReadRepositoryFile(
            "web/src/views/other/execution-history/components/ExecutionHistorySmartFillPlayback.vue");
        smartFillPlaybackContent.Should().Contain("statusOptions");
        smartFillPlaybackContent.Should().Contain("getMatchOriginText");
        smartFillPlaybackContent.Should().Contain("完全匹配");
        smartFillPlaybackContent.Should().Contain("AI匹配");
        smartFillPlaybackContent.Should().Contain("未采用");
        smartFillPlaybackContent.Should().Contain("未匹配");
        smartFillPlaybackContent.Should().Contain("executionSnapshot.finalAcceptance");
        smartFillPlaybackContent.Should().Contain("executionSnapshot.finalRemark");

        var batchReplyContent = ReadRepositoryFile(
            "web/src/views/other/execution-history/components/ExecutionHistoryBatchReplyDetail.vue");
        batchReplyContent.Should().Contain("formatConfidence");
        batchReplyContent.Should().Contain("confidencePercent");
        batchReplyContent.Should().Contain("prop=\"acceptance\"");
        batchReplyContent.Should().Contain("prop=\"remark\"");
    }

    [Fact]
    public void NavigationAndFrontend_ShouldExposeSmartFillArchivePage()
    {
        var manifest = ReadRepositoryFile("shared/navigation/navigation-manifest.json");
        manifest.Should().Contain("\"id\": \"other-smart-fill-archives\"");
        manifest.Should().Contain("\"code\": \"page:other:smart-fill-archives\"");
        manifest.Should().Contain("\"path\": \"/other/smart-fill-archives\"");

        var route = ReadRepositoryFile("web/src/router/modules/other.ts");
        route.Should().Contain("name: \"SmartFillArchives\"");
        route.Should().Contain("@/views/other/smart-fill-archives/index.vue");
        route.Should().Contain("getPagePermission(\"other-smart-fill-archives\")");

        var api = ReadRepositoryFile("web/src/api/execution-history.ts");
        api.Should().Contain("getSmartFillArchiveList");
        api.Should().Contain("downloadSmartFillArchive");
        api.Should().Contain("smart-fill-archives");

        var page = ReadRepositoryFile("web/src/views/other/smart-fill-archives/index.vue");
        page.Should().Contain("填充存档");
        page.Should().Contain("来源文件");
        page.Should().Contain("所属部门");
        page.Should().Contain("操作人");
        page.Should().Contain("当前页");
        page.Should().Contain("下载");
        page.Should().NotContain("el-drawer");
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
