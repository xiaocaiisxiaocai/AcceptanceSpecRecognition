using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class BatchReplyFrontendRegressionTests
{
    [Fact]
    public void NavigationManifest_ShouldExposeBatchReplyMenuAndPage()
    {
        var content = ReadRepositoryFile("shared/navigation/navigation-manifest.json");

        content.Should().Contain("\"id\": \"batch-reply\"");
        content.Should().Contain("\"code\": \"menu:batch-reply\"");
        content.Should().Contain("\"title\": \"批量回复\"");
        content.Should().Contain("\"path\": \"/batch-reply\"");
        content.Should().Contain("\"id\": \"batch-reply-index\"");
        content.Should().Contain("\"code\": \"page:batch-reply:index\"");
        content.Should().Contain("\"path\": \"/batch-reply/index\"");
    }

    [Fact]
    public void BatchReplyRoute_And_Page_ShouldUseExpectedPermissions()
    {
        var routeContent = ReadRepositoryFile("web/src/router/modules/batch-reply.ts");
        routeContent.Should().Contain("path: \"/batch-reply\"");
        routeContent.Should().Contain("name: \"BatchReply\"");
        routeContent.Should().Contain("component: () => import(\"@/views/batch-reply/index.vue\")");
        routeContent.Should().Contain("permissions: getMenuPermission(\"batch-reply\")");
        routeContent.Should().Contain("permissions: getPagePermission(\"batch-reply-index\")");

        var pageContent = ReadRepositoryFile("web/src/views/batch-reply/index.vue");
        pageContent.Should().Contain("btn:batch-reply:preview");
        pageContent.Should().Contain("btn:batch-reply:execute");
        pageContent.Should().Contain("uploadBatchReplySource");
        pageContent.Should().Contain("previewBatchReply");
        pageContent.Should().Contain("executeBatchReply");
        pageContent.Should().Contain("downloadBatchReplyResult");
        pageContent.Should().Contain("v-if=\"canUploadSourceFile && !sourceFile\"", "未上传来源文件时，上传区应受来源文件上传权限控制");
        pageContent.Should().Contain("当前账号没有来源文件上传权限", "无上传权限时应给出明确提示，而不是继续展示上传入口");
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
