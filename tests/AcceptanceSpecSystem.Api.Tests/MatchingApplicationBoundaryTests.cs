using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class MatchingApplicationBoundaryTests
{
    private static readonly string[] ApplicationOwnedServices =
    [
        "MatchingPreviewAppService.cs",
        "MatchingLlmStreamAppService.cs",
        "MatchingFillExecutionAppService.cs",
        "MatchingTaskAppService.cs",
        "MatchingTaskSnapshotService.cs",
        "MatchingWorkflowSupportService.cs",
        "ExecutionHistoryAppService.cs",
        "SmartFillSpecBackfillAppService.cs"
    ];

    [Fact]
    public void MatchingAndExecutionHistoryUseCases_ShouldBeOwnedByApplication()
    {
        foreach (var fileName in ApplicationOwnedServices)
        {
            File.Exists(Path.Combine(Root, "src", "AcceptanceSpecSystem.Application", "Services", fileName))
                .Should().BeTrue($"{fileName} 应由 Application 层拥有");
            File.Exists(Path.Combine(Root, "src", "AcceptanceSpecSystem.Api", "Services", fileName))
                .Should().BeFalse($"{fileName} 不应继续保留 Api 编排实现");
        }
    }

    [Fact]
    public void ApplicationMatchingUseCases_ShouldNotReferenceAspNetProtocolTypes()
    {
        var serviceDirectory = Path.Combine(Root, "src", "AcceptanceSpecSystem.Application", "Services");
        var files = Directory.GetFiles(serviceDirectory, "*.cs")
            .Where(path =>
                Path.GetFileName(path).StartsWith("Matching", StringComparison.Ordinal) ||
                Path.GetFileName(path).StartsWith("ExecutionHistory", StringComparison.Ordinal) ||
                Path.GetFileName(path).StartsWith("SmartFillSpecBackfill", StringComparison.Ordinal));

        foreach (var path in files)
        {
            var content = File.ReadAllText(path);
            content.Should().NotContain("ClaimsPrincipal");
            content.Should().NotContain("HttpResponse");
            content.Should().NotContain("HttpContext");
            content.Should().NotContain("IFormFile");
            content.Should().NotContain("AcceptanceSpecSystem.Api");
            content.Should().NotContain("Microsoft.AspNetCore");
        }

        Read("src/AcceptanceSpecSystem.Application/Services/MatchingWorkflowSupportService.Sse.cs")
            .Should().Contain("IMatchingEventStream");
    }

    [Fact]
    public void ApiControllers_ShouldOnlyMapClaimsHttpAndSse()
    {
        var controllers = new[]
        {
            "MatchingPreviewController.cs",
            "MatchingExecutionController.cs",
            "MatchingTaskController.cs",
            "ExecutionHistoryController.cs"
        };

        foreach (var fileName in controllers)
        {
            var content = Read($"src/AcceptanceSpecSystem.Api/Controllers/{fileName}");
            content.Should().NotContain("IUnitOfWork");
            content.Should().NotContain("AppDbContext");
            content.Should().NotMatchRegex(@"\bI[A-Za-z0-9]+Repository\b");
        }

        Read("src/AcceptanceSpecSystem.Api/Controllers/MatchingExecutionController.cs")
            .Should().Contain("HttpMatchingEventStream");
        Read("src/AcceptanceSpecSystem.Api/Controllers/MatchingApiControllerBase.cs")
            .Should().Contain("GetMatchingUserContext");
    }

    [Fact]
    public void ApiInfrastructureAdapters_ShouldImplementApplicationPorts()
    {
        Read("src/AcceptanceSpecSystem.Api/Services/MatchingResultWriteBackService.cs")
            .Should().Contain("IMatchingResultWriteBackPort");
        Read("src/AcceptanceSpecSystem.Api/Services/SpecEmbeddingCacheService.cs")
            .Should().Contain("IMatchingEmbeddingCache");
        Read("src/AcceptanceSpecSystem.Api/Services/HttpMatchingEventStream.cs")
            .Should().Contain("IMatchingEventStream");
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string Root
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "AcceptanceSpecSystem.sln"))) return current.FullName;
                current = current.Parent;
            }

            throw new InvalidOperationException("未找到仓库根目录");
        }
    }
}
