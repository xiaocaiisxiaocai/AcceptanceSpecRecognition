using AcceptanceSpecSystem.Data.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AcceptanceSpecSystem.Data.Tests;

public class MigrationMetadataTests
{
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
}
