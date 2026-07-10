using FluentAssertions;

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
