using FluentAssertions;
using System.Text.RegularExpressions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class ProductionDeploymentDocumentationTests
{
    [Fact]
    public void ProductionDeploymentDocuments_ShouldUseHealthChecksInsteadOfSwagger()
    {
        var dockerDoc = ReadFile("docs/DEPLOY-DOCKER.md");
        var iisDoc = ReadFile("docs/DEPLOY-IIS.md");
        var windowsDockerDoc = ReadFile("docs/DEPLOY-WINDOWS-DOCKER.md");

        dockerDoc.Should().Contain("http://localhost:5290/health");
        dockerDoc.Should().NotContain("/swagger");
        iisDoc.Should().Contain("/api/health");
        iisDoc.Should().NotContain("/api/swagger");
        windowsDockerDoc.Should().Contain("/health");
        windowsDockerDoc.Should().NotContain("/swagger");
    }

    [Fact]
    public void Program_ShouldOnlyEnableSwaggerInDevelopment()
    {
        var program = ReadFile("src/AcceptanceSpecSystem.Api/Program.cs")
            .Replace("\r\n", "\n");

        program.Should().Contain("""
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(options =>
            """);
    }

    [Fact]
    public void ImageDeploymentTemplate_ShouldProvideRequiredAuthSeedAndPersistentBackups()
    {
        var compose = ReadFile(".deploy/docker-compose.images.yml");
        var environment = ReadEnvironmentExample(".deploy/production.env.example");
        var deploymentGuide = ReadFile(".deploy/README.md");
        var releaseScript = ReadFile(".deploy/Publish-DockerImageRelease.ps1");

        compose.Should().Contain("${MYSQL_ROOT_PASSWORD:?MYSQL_ROOT_PASSWORD must be set}");
        compose.Should().Contain("${MYSQL_PASSWORD:?MYSQL_PASSWORD must be set}");
        compose.Should().Contain("${JWT_SIGNING_KEY:?JWT_SIGNING_KEY must be set}");
        compose.Should().Contain("${AUTH_SEED_ADMIN_PASSWORD:?AUTH_SEED_ADMIN_PASSWORD must be set}");
        compose.Should().Contain("${AUTH_SEED_COMMON_PASSWORD:?AUTH_SEED_COMMON_PASSWORD must be set}");
        compose.Should().Contain("replace_with_*|changethis*|ChangeThis*");
        compose.Should().Contain("DatabaseBackup__BackupDirectory: /app/backups");
        compose.Should().Contain("- api-backups:/app/backups");
        compose.Should().Contain("api-backups:");

        foreach (var key in new[]
                 {
                     "MYSQL_ROOT_PASSWORD",
                     "MYSQL_PASSWORD",
                     "JWT_SIGNING_KEY",
                     "AUTH_SEED_ADMIN_PASSWORD",
                     "AUTH_SEED_COMMON_PASSWORD"
                 })
        {
            environment[key].Should().BeEmpty($"{key} 的示例值必须不可直接部署");
        }

        deploymentGuide.Should().Contain("AUTH_SEED_ADMIN_PASSWORD");
        deploymentGuide.Should().Contain("AUTH_SEED_COMMON_PASSWORD");
        deploymentGuide.Should().Contain("api-backups");
        deploymentGuide.Should().Contain("down -v");
        deploymentGuide.Should().Contain("异机或对象存储");
        releaseScript.Should().Contain("api-backups");
        releaseScript.Should().Contain("down -v");
        deploymentGuide.Should().Contain("sh validate-production-env.sh .env");
        releaseScript.Should().Contain("sh validate-production-env.sh .env");
        deploymentGuide.Should().NotContain("grep -E");
        releaseScript.Should().NotContain("grep -E");

        var validator = ReadFile("deploy/validate-production-env.sh");
        validator.Should().Contain("is_placeholder");
        validator.Should().Contain("replace_with_*");
        validator.Should().Contain("未回显任何配置值");
    }

    [Fact]
    public void Ci_ShouldRunPinnedOpenSpecStrictValidation()
    {
        var workflow = ReadFile(".github/workflows/ci.yml");

        workflow.Should().Contain("@fission-ai/openspec@1.5.0");
        workflow.Should().Contain("openspec validate --all --strict --no-interactive");
        workflow.Should().NotContain("feat/add-ai-equivalence-adjudication");
    }

    [Fact]
    public void RuntimeImages_ShouldBeImmutableNonRootAndReadOnly()
    {
        var apiDockerfile = ReadFile("src/AcceptanceSpecSystem.Api/Dockerfile");
        var webDockerfile = ReadFile("web/Dockerfile");
        var localCompose = ReadFile("docker-compose.yml");
        var releaseCompose = ReadFile(".deploy/docker-compose.images.yml");
        var nginx = ReadFile("deploy/nginx/default.conf");
        var workflow = ReadFile(".github/workflows/ci.yml");
        var maintenanceGuide = ReadFile("docs/CONTAINER-DEPENDENCY-UPDATES.md");

        Regex.Matches(apiDockerfile, @"(?m)^FROM [^\r\n ]+@sha256:[0-9a-f]{64}")
            .Should().HaveCount(2);
        Regex.Matches(webDockerfile, @"(?m)^FROM [^\r\n ]+@sha256:[0-9a-f]{64}")
            .Should().HaveCount(2);
        apiDockerfile.Should().Contain("USER $APP_UID");
        webDockerfile.Should().Contain("nginxinc/nginx-unprivileged:1.28-alpine@sha256:");
        webDockerfile.Should().Contain("USER nginx");
        webDockerfile.Should().Contain("EXPOSE 8080");
        webDockerfile.Should().Contain("http://127.0.0.1:8080/");
        webDockerfile.Should().NotContain("http://localhost:8080/",
            "Alpine 会优先把 localhost 解析为 IPv6，而 Nginx 当前仅监听 IPv4");
        nginx.Should().Contain("listen 8080;");
        nginx.Should().Contain("location = /logout");
        nginx.Should().Contain("proxy_pass http://api:8080/logout;");

        foreach (var compose in new[] { localCompose, releaseCompose })
        {
            compose.Should().Contain("mysql:8.0@sha256:");
            Regex.Matches(compose, @"(?m)^\s{4}read_only: true$")
                .Should().HaveCount(2, "API 与 Web runtime 都必须使用只读根文件系统");
            compose.Should().Contain("/tmp:size=256m,mode=1777");
            compose.Should().Contain("/var/cache/nginx:size=32m,mode=0755");
        }

        workflow.Should().Contain("Verify immutable bases and non-root runtime");
        workflow.Should().Contain("docker image inspect acceptance-spec-api:ci");
        workflow.Should().Contain("--read-only");
        workflow.Should().Contain("touch /data/files/.write-test");
        maintenanceGuide.Should().Contain("digest 更新必须作为显式依赖更新提交");
        maintenanceGuide.Should().Contain("已有卷升级");

        var imageDeploymentGuide = ReadFile(".deploy/README.md");
        var windowsDeploymentGuide = ReadFile("docs/DEPLOY-WINDOWS-DOCKER.md");
        imageDeploymentGuide.Should().Contain("BROWSER_AUTH_ALLOW_INSECURE_HTTP=true");
        imageDeploymentGuide.Should().Contain("SameSite=Strict");
        imageDeploymentGuide.Should().Contain("HTTP 是明文传输");
        imageDeploymentGuide.Should().NotContain("http://134.175.195.207");
        windowsDeploymentGuide.Should().Contain("内网同站 HTTP");
        windowsDeploymentGuide.Should().Contain("BROWSER_AUTH_ALLOW_INSECURE_HTTP=true");
        releaseCompose.Should().Contain("BrowserAuth__AllowInsecureHttp");
        releaseCompose.Should().Contain("127.0.0.1:${API_HOST_PORT}:8080");
    }

    [Fact]
    public void CurrentDeploymentDocuments_ShouldReferenceMainBranch()
    {
        var documentationIndex = ReadFile("docs/README.md");
        var windowsDockerGuide = ReadFile("docs/DEPLOY-WINDOWS-DOCKER.md");
        var imageDeploymentGuide = ReadFile(".deploy/README.md");

        documentationIndex.Should().Contain("`main` 分支统一管理");
        documentationIndex.Should().NotContain("`develop` 分支管理");
        windowsDockerGuide.Should().Contain("git switch main");
        windowsDockerGuide.Should().Contain("git pull --ff-only origin main");
        windowsDockerGuide.Should().NotContain("feat/add-ai-equivalence-adjudication");
        imageDeploymentGuide.Should().NotContain("feat/add-ai-equivalence-adjudication");
    }

    private static IReadOnlyDictionary<string, string> ReadEnvironmentExample(string relativePath)
    {
        return ReadFile(relativePath)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('#'))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length == 2 ? parts[1] : string.Empty);
    }

    private static string ReadFile(string relativePath)
    {
        return File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcceptanceSpecSystem.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位仓库根目录");
    }
}
