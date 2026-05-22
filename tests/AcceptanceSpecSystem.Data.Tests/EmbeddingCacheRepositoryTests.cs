using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Tests;

public class EmbeddingCacheRepositoryTests : TestBase
{
    [Fact]
    public async Task GetBySpecIdsAndModelAndUsageAsync_ShouldOnlyReturnMatchingUsage()
    {
        var repository = new EmbeddingCacheRepository(Context);

        Context.EmbeddingCaches.AddRange(
            new EmbeddingCache
            {
                SpecId = 1,
                ModelName = "embedding-model",
                Usage = "matching",
                TextHash = "hash-matching",
                Vector = [1]
            },
            new EmbeddingCache
            {
                SpecId = 1,
                ModelName = "embedding-model",
                Usage = "semantic-search",
                TextHash = "hash-search",
                Vector = [2]
            },
            new EmbeddingCache
            {
                SpecId = 2,
                ModelName = "embedding-model",
                Usage = "matching",
                TextHash = "hash-matching-2",
                Vector = [3]
            });
        await Context.SaveChangesAsync();

        var result = await repository.GetBySpecIdsAndModelAndUsageAsync(
            [1, 2],
            "embedding-model",
            "semantic-search");

        result.Should().ContainSingle();
        result[0].SpecId.Should().Be(1);
        result[0].Usage.Should().Be("semantic-search");
    }

    [Fact]
    public async Task EmbeddingCache_ShouldAllowSameSpecAndModelWithDifferentUsage()
    {
        Context.EmbeddingCaches.AddRange(
            new EmbeddingCache
            {
                SpecId = 10,
                ModelName = "embedding-model",
                Usage = "matching",
                TextHash = "hash-a",
                Vector = [1]
            },
            new EmbeddingCache
            {
                SpecId = 10,
                ModelName = "embedding-model",
                Usage = "semantic-search",
                TextHash = "hash-b",
                Vector = [2]
            });

        await Context.SaveChangesAsync();

        var caches = await Context.EmbeddingCaches
            .Where(cache => cache.SpecId == 10 && cache.ModelName == "embedding-model")
            .OrderBy(cache => cache.Usage)
            .ToListAsync();

        caches.Should().HaveCount(2);
        caches.Select(cache => cache.Usage).Should().BeEquivalentTo(["matching", "semantic-search"]);
    }

    [Fact]
    public void EmbeddingCache_Model_ShouldRequireUsageAndTextHashWithExpectedIndex()
    {
        var entityType = Context.Model.FindEntityType(typeof(EmbeddingCache));
        entityType.Should().NotBeNull();

        new EmbeddingCache().Usage.Should().Be(EmbeddingCache.DefaultUsage);
        new EmbeddingCache().TextHash.Should().BeEmpty();
        entityType!.FindProperty(nameof(EmbeddingCache.Usage))!.IsNullable.Should().BeFalse();
        entityType.FindProperty(nameof(EmbeddingCache.Usage))!.GetMaxLength().Should().Be(64);
        entityType.FindProperty(nameof(EmbeddingCache.Usage))!.GetDefaultValue().Should().Be(EmbeddingCache.DefaultUsage);
        entityType.FindProperty(nameof(EmbeddingCache.TextHash))!.IsNullable.Should().BeFalse();
        entityType.FindProperty(nameof(EmbeddingCache.TextHash))!.GetMaxLength().Should().Be(128);
        entityType.FindProperty(nameof(EmbeddingCache.TextHash))!.GetDefaultValue().Should().Be(string.Empty);

        var uniqueIndex = entityType.GetIndexes()
            .Single(index => index.IsUnique);

        uniqueIndex.Properties
            .Select(property => property.Name)
            .Should()
            .Equal(nameof(EmbeddingCache.SpecId), nameof(EmbeddingCache.ModelName), nameof(EmbeddingCache.Usage));
    }
}
