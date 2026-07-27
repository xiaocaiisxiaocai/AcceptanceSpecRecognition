using System.Reflection;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace AcceptanceSpecSystem.Data.Tests;

public class EmbeddingCacheRepositoryTests : TestBase
{
    private const string EmbeddingCacheUniqueIndex = "IX_EmbeddingCaches_SpecId_ModelName_Usage";

    [Fact]
    public void 唯一约束分类器_应只识别嵌套的MySql重复键错误()
    {
        var duplicateKeyException = new DbUpdateException(
            "缓存写入失败",
            new InvalidOperationException(
                "provider error",
                CreateMySqlException(
                    MySqlErrorCode.DuplicateKeyEntry,
                    $"Duplicate entry for key '{EmbeddingCacheUniqueIndex}'")));
        var foreignKeyException = new DbUpdateException(
            "缓存写入失败",
            CreateMySqlException(
                MySqlErrorCode.NoReferencedRow2,
                $"Cannot add or update a child row for key '{EmbeddingCacheUniqueIndex}'"));

        DatabaseConstraintClassifier.IsUniqueViolation(duplicateKeyException).Should().BeTrue();
        DatabaseConstraintClassifier.IsUniqueViolation(foreignKeyException).Should().BeFalse();
        DatabaseConstraintClassifier
            .IsUniqueViolation(foreignKeyException, EmbeddingCacheUniqueIndex)
            .Should()
            .BeFalse();
        DatabaseConstraintClassifier.IsUniqueViolation(new DbUpdateException("普通数据库错误")).Should().BeFalse();
    }

    [Theory]
    [InlineData("Duplicate entry 'value' for key 'IX_EmbeddingCaches_SpecId_ModelName_Usage'")]
    [InlineData("Duplicate entry 'value' for key \"EmbeddingCaches.IX_EmbeddingCaches_SpecId_ModelName_Usage\"")]
    [InlineData("Duplicate entry 'value' for key `acceptance.EmbeddingCaches.IX_EmbeddingCaches_SpecId_ModelName_Usage`")]
    public void 唯一约束分类器_应从ForKey子句精确识别目标索引(string message)
    {
        var exception = new DbUpdateException(
            "缓存写入失败",
            CreateMySqlException(
                MySqlErrorCode.DuplicateKeyEntry,
                message));

        DatabaseConstraintClassifier
            .IsUniqueViolation(exception, EmbeddingCacheUniqueIndex)
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData("Duplicate entry 'value' for key 'IX_EmbeddingCaches_SpecId_ModelName_Usage_Extra'")]
    [InlineData("Duplicate entry 'value' for key 'Prefix_IX_EmbeddingCaches_SpecId_ModelName_Usage'")]
    [InlineData("Duplicate entry 'IX_EmbeddingCaches_SpecId_ModelName_Usage' for key 'IX_EmbeddingCaches_Other'")]
    [InlineData("Duplicate entry 'IX_EmbeddingCaches_SpecId_ModelName_Usage'")]
    [InlineData("Duplicate entry 'value' for key IX_EmbeddingCaches_SpecId_ModelName_Usage")]
    public void 唯一约束分类器_不应接受相似索引重复值诱导或异常格式(string message)
    {
        var exception = new DbUpdateException(
            "缓存写入失败",
            CreateMySqlException(
                MySqlErrorCode.DuplicateKeyEntry,
                message));

        DatabaseConstraintClassifier
            .IsUniqueViolation(exception, EmbeddingCacheUniqueIndex)
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task 按规格模型用途读取_应精确匹配且不跟踪结果()
    {
        var repository = new EmbeddingCacheRepository(Context);
        Context.EmbeddingCaches.AddRange(
            new EmbeddingCache
            {
                SpecId = 21,
                ModelName = "embedding-model",
                Usage = "matching",
                TextHash = "hash-matching",
                Vector = [1]
            },
            new EmbeddingCache
            {
                SpecId = 21,
                ModelName = "embedding-model",
                Usage = "semantic-search",
                TextHash = "hash-search",
                Vector = [2]
            });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var result = await repository.GetBySpecModelUsageAsync(
            21,
            "embedding-model",
            "semantic-search",
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.TextHash.Should().Be("hash-search");
        Context.Entry(result).State.Should().Be(EntityState.Detached);
    }

    [Fact]
    public async Task 按规格模型用途读取_应传递取消令牌()
    {
        var repository = new EmbeddingCacheRepository(Context);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var action = () => repository.GetBySpecModelUsageAsync(
            21,
            "embedding-model",
            "semantic-search",
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

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

    private static MySqlException CreateMySqlException(MySqlErrorCode errorCode, string message)
    {
        var constructor = typeof(MySqlException).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(MySqlErrorCode), typeof(string), typeof(string), typeof(Exception)],
            modifiers: null);

        constructor.Should().NotBeNull("MySqlConnector 2.3.5 应保留 provider 异常构造契约");
        return (MySqlException)constructor!.Invoke([errorCode, "23000", message, null]);
    }
}
