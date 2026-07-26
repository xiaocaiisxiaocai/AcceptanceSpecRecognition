using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AcceptanceSpecSystem.Data.Tests;

public class MigrationMetadataTests
{
    [Fact]
    public void AcceptanceSpecReferenceForeignKeys_ShouldRestrictPrincipalDeletes()
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"migration-metadata-{Guid.NewGuid():N}")
                .Options);

        var entityType = context.Model.FindEntityType(typeof(AcceptanceSpec))!;
        var protectedPrincipalTypes = new[] { typeof(Customer), typeof(Process), typeof(MachineModel) };

        foreach (var principalType in protectedPrincipalTypes)
        {
            var foreignKey = entityType.GetForeignKeys()
                .Single(key => key.PrincipalEntityType.ClrType == principalType);
            foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
        }
    }

    [Fact]
    public void EveryMigrationTargetModel_ShouldBuildWithoutInvalidMetadata()
    {
        var migrationTypes = typeof(AppDbContext).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(Migration).IsAssignableFrom(type))
            .OrderBy(type => type.FullName)
            .ToArray();

        migrationTypes.Should().NotBeEmpty();

        foreach (var migrationType in migrationTypes)
        {
            var migration = (Migration)Activator.CreateInstance(migrationType, nonPublic: true)!;

            var buildTargetModel = () => migration.TargetModel.GetEntityTypes().ToArray();

            buildTargetModel.Should().NotThrow($"迁移 {migrationType.Name} 的目标模型必须可用于生成迁移 SQL");
        }
    }

    /// <summary>
    /// [R-28] DatabaseInitializer 的破坏性迁移分类基线依赖手工维护的静态列表，
    /// 新增迁移若遗漏分类会导致 <see cref="DatabaseInitializer.ClassifyMigration"/> 静默返回
    /// <see cref="DatabaseMigrationRisk.Unclassified"/>。这里强制校验 Migrations 目录下的
    /// 每一个迁移都必须被显式分类为 Safe 或 Destructive，防止新迁移遗漏分类。
    /// </summary>
    [Fact]
    public void EveryMigration_ShouldBeExplicitlyClassifiedAsSafeOrDestructive()
    {
        var migrationIds = typeof(AppDbContext).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(Migration).IsAssignableFrom(type))
            .Select(type => ((MigrationAttribute)Attribute.GetCustomAttribute(type, typeof(MigrationAttribute))!).Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        migrationIds.Should().NotBeEmpty();

        var unclassified = migrationIds
            .Where(id => DatabaseInitializer.ClassifyMigration(id) == DatabaseMigrationRisk.Unclassified)
            .ToArray();

        unclassified.Should().BeEmpty(
            "Migrations 目录下的每个迁移都必须在 DatabaseInitializer 的 " +
            "DestructiveMigrationIds 或 SafeMigrationIds 静态列表中显式分类，" +
            $"以下迁移遗漏分类: {string.Join(", ", unclassified)}");
    }
}
