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
        pageContent.Should().Contain("置信度");
        pageContent.Should().Contain("人工选择");
        pageContent.Should().Contain("文件");
        pageContent.Should().Contain("Sheet");
        pageContent.Should().Contain("getExecutionHistoryList");
        pageContent.Should().Contain("getExecutionHistoryDetail");
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
