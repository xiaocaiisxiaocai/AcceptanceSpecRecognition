using System.Text.RegularExpressions;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class PhaseTwoFinalBoundaryTests
{
    [Fact]
    public void ProtocolLayer_ShouldNotDependOnPersistenceOrConcreteInfrastructureAdapters()
    {
        var apiRoot = Path.Combine(GetRepositoryRoot(), "src", "AcceptanceSpecSystem.Api");
        var forbiddenAdapters = new[]
        {
            "DocumentFileAccessService",
            "DocumentTableAccessService",
            "FileStorageService",
            "MatchingResultWriteBackService",
            "SpecEmbeddingCacheService",
            "DatabaseBackupExecutor",
            "AppDbContext"
        };

        foreach (var directoryName in new[] { "Controllers", "Filters", "Middleware", "Authorization" })
        {
            var directory = Path.Combine(apiRoot, directoryName);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var sourceFile in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var content = File.ReadAllText(sourceFile);
                var relativePath = Path.GetRelativePath(apiRoot, sourceFile);

                content.Should().NotContain("IUnitOfWork", $"{relativePath} 属于协议层");
                content.Should().NotContain("AcceptanceSpecSystem.Data.Repositories", $"{relativePath} 属于协议层");
                Regex.IsMatch(content, @"\bI[A-Za-z0-9]+Repository\b").Should().BeFalse(
                    $"{relativePath} 属于协议层");

                foreach (var adapter in forbiddenAdapters)
                {
                    content.Should().NotContain(adapter, $"{relativePath} 应依赖应用端口而不是具体基础设施适配器");
                }
            }
        }
    }

    [Fact]
    public void ApplicationLayer_ShouldNotDependOnApiOrAspNetProtocolTypes()
    {
        var applicationRoot = Path.Combine(GetRepositoryRoot(), "src", "AcceptanceSpecSystem.Application");
        var forbiddenTokens = new[]
        {
            "AcceptanceSpecSystem.Api",
            "Microsoft.AspNetCore",
            "ClaimsPrincipal",
            "HttpContext",
            "HttpResponse",
            "IFormFile"
        };

        foreach (var sourceFile in Directory.GetFiles(applicationRoot, "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(sourceFile);
            var relativePath = Path.GetRelativePath(applicationRoot, sourceFile);
            foreach (var token in forbiddenTokens)
            {
                content.Should().NotContain(token, $"{relativePath} 属于 Application 层");
            }
        }
    }

    [Fact]
    public void ApiServicePersistenceAdapters_ShouldMatchReviewedAllowlist()
    {
        var servicesRoot = Path.Combine(GetRepositoryRoot(), "src", "AcceptanceSpecSystem.Api", "Services");
        var reviewedAllowlist = new HashSet<string>(StringComparer.Ordinal)
        {
            "DatabaseHealthCheck.cs",
            "BatchReplyCleanupFileStore.cs",
            "DocumentFileAccessService.cs",
            "MySqlBatchReplyDistributedLockProvider.cs",
            "SingleCompanyHealthCheck.cs",
            "SmartConfigurationFileAccessService.cs",
            "SpecEmbeddingCacheService.cs"
        };

        var persistenceDependentFiles = Directory.GetFiles(servicesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var content = File.ReadAllText(path);
                return content.Contains("IUnitOfWork", StringComparison.Ordinal)
                       || content.Contains("AppDbContext", StringComparison.Ordinal)
                       || content.Contains("AcceptanceSpecSystem.Data.Repositories", StringComparison.Ordinal)
                       || Regex.IsMatch(content, @"\bI[A-Za-z0-9]+Repository\b");
            })
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal);

        persistenceDependentFiles.Should().BeEquivalentTo(reviewedAllowlist,
            "API Services 仅允许健康检查以及文件/缓存持久化 adapter 直接接触持久化实现");
    }

    [Fact]
    public void MigrationFacadesAndApiBusinessOrchestrators_ShouldBeRemoved()
    {
        var removedPaths = new[]
        {
            "src/AcceptanceSpecSystem.Application/Services/MatchingExecutionAppService.cs",
            "src/AcceptanceSpecSystem.Application/Services/MatchingWorkflowService.cs",
            "src/AcceptanceSpecSystem.Api/Services/SpecSemanticSearchService.cs",
            "src/AcceptanceSpecSystem.Api/Services/SpecDuplicateDetectionService.cs",
            "src/AcceptanceSpecSystem.Api/Services/EmbeddingCacheWarmupManager.cs"
        };

        foreach (var relativePath in removedPaths)
        {
            File.Exists(Path.Combine(GetRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Should().BeFalse($"迁移期实现 {relativePath} 不应继续存在");
        }
    }

    [Theory]
    [InlineData("AuditLogCleanupService.cs", "IAuditLogRetentionAppService")]
    [InlineData("EmbeddingCacheCleanupService.cs", "IEmbeddingCacheRetentionAppService")]
    public void CleanupHostedServices_ShouldBeThinApplicationAdapters(string fileName, string applicationPort)
    {
        var path = Path.Combine(GetRepositoryRoot(), "src", "AcceptanceSpecSystem.Api", "Services", fileName);
        var content = File.ReadAllText(path);

        content.Should().Contain(applicationPort);
        content.Should().NotContain("IUnitOfWork");
        content.Should().NotContain("AcceptanceSpecSystem.Data.Repositories");
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcceptanceSpecSystem.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new DirectoryNotFoundException("无法定位仓库根目录");
    }
}
