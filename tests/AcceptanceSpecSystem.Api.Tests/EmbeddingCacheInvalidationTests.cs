using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class EmbeddingCacheInvalidationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public EmbeddingCacheInvalidationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public void EmbeddingCache_ShouldSeparateSemanticSearchAndSmartMatchingUsage()
    {
        var entityContent = ReadFileText("src/AcceptanceSpecSystem.Data/Entities/EmbeddingCache.cs");
        var repositoryContent = ReadFileText("src/AcceptanceSpecSystem.Data/Repositories/EmbeddingCacheRepository.cs");
        var cacheServiceContent = ReadFileText("src/AcceptanceSpecSystem.Api/Services/SpecEmbeddingCacheService.cs");
        var semanticSearchContent = ReadFileText("src/AcceptanceSpecSystem.Api/Services/SpecSemanticSearchService.cs");
        var matchingCandidateProviderContent = ReadFileText("src/AcceptanceSpecSystem.Api/Services/MatchingCandidateProvider.cs");

        entityContent.Should().Contain(
            "Usage",
            "同一规格在语义搜索和智能匹配中使用不同文本生成向量，缓存键必须包含 usage");
        repositoryContent.Should().Contain(
            "GetBySpecIdsAndModelAndUsageAsync",
            "缓存查询不能只按 SpecId + ModelName 命中，否则不同用途会互相复用错误向量");
        cacheServiceContent.Should().Contain(
            "GetBySpecIdsAndModelAndUsageAsync",
            "统一缓存服务读取缓存时应显式带上 usage");
        cacheServiceContent.Should().Contain(
            "Usage =",
            "统一缓存服务写入缓存时应显式标记 usage");
        semanticSearchContent.Should().Contain(
            "EmbeddingCacheUsages.SemanticSearch",
            "语义搜索应使用独立 usage");
        matchingCandidateProviderContent.Should().Contain(
            "HydrateMatchingCandidatesAsync",
            "智能匹配预览和执行链路应通过统一候选提供器使用独立 usage");
    }

    [Fact]
    public async Task UpdateSpec_WhenSearchTextChanges_ShouldRemoveEmbeddingCachesForSpec()
    {
        var setup = await SeedSpecWithEmbeddingCachesAsync();

        using var response = await _client.PutAsync(
            $"/api/specs/{setup.SpecId}",
            ApiClientJson.ToJsonContent(new
            {
                project = "更新项目",
                specification = "更新规格",
                acceptance = "更新验收",
                remark = "更新备注"
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cacheExists = await db.EmbeddingCaches.AnyAsync(cache => cache.SpecId == setup.SpecId);
        cacheExists.Should().BeFalse("规格文本更新后旧向量已失效，应清理该规格的全部 EmbeddingCaches");
    }

    private async Task<SpecCacheSetup> SeedSpecWithEmbeddingCachesAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var customer = new Customer { Name = $"缓存客户-{suffix}", CreatedAt = DateTime.UtcNow };
        var process = new Process { Name = $"缓存制程-{suffix}", CreatedAt = DateTime.UtcNow };
        var wordFile = new WordFile
        {
            FileName = $"cache-{suffix}.docx",
            FileContent = [],
            FileHash = Guid.NewGuid().ToString("N"),
            UploadedAt = DateTime.UtcNow
        };

        db.Customers.Add(customer);
        db.Processes.Add(process);
        db.WordFiles.Add(wordFile);
        await db.SaveChangesAsync();

        var spec = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            ProcessId = process.Id,
            Project = "原项目",
            Specification = "原规格",
            Acceptance = "原验收",
            Remark = "原备注",
            WordFileId = wordFile.Id,
            OwnerOrgUnitId = 1,
            CreatedByUserId = 1,
            ImportedAt = DateTime.UtcNow
        };

        db.AcceptanceSpecs.Add(spec);
        await db.SaveChangesAsync();

        db.EmbeddingCaches.AddRange(
            new EmbeddingCache
            {
                SpecId = spec.Id,
                ModelName = "test-embedding",
                Vector = [1, 2, 3, 4],
                CreatedAt = DateTime.UtcNow
            },
            new EmbeddingCache
            {
                SpecId = spec.Id,
                ModelName = "another-test-embedding",
                Vector = [5, 6, 7, 8],
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        return new SpecCacheSetup(spec.Id);
    }

    private static string ReadFileText(
        string relativePath,
        [CallerFilePath] string callerFilePath = "")
    {
        var repositoryRoot = GetRepositoryRoot(callerFilePath);
        return File.ReadAllText(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n");
    }

    private static string GetRepositoryRoot(string callerFilePath)
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(callerFilePath) ?? AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AcceptanceSpecSystem.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("未找到仓库根目录");
    }

    private sealed record SpecCacheSetup(int SpecId);
}
