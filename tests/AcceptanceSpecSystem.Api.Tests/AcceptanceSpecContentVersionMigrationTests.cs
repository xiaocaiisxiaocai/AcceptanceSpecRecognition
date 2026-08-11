using System.Reflection;
using AcceptanceSpecSystem.Data;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class AcceptanceSpecContentVersionMigrationTests
{
    private const string MigrationTypeName =
        "AcceptanceSpecSystem.Data.Migrations.AddAcceptanceSpecContentVersionHistory";
    private const string MigrationId =
        "20260811033921_AddAcceptanceSpecContentVersionHistory";

    [Fact]
    public void Migration_ShouldCreateSnapshotTableAndBackfillOnlyCurrentVersion()
    {
        var migrationType = typeof(AppDbContext).Assembly.GetType(MigrationTypeName);
        migrationType.Should().NotBeNull();
        var migration = (Migration)Activator.CreateInstance(migrationType!)!;
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        migrationType!
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        builder.Operations.OfType<CreateTableOperation>()
            .Should().ContainSingle(operation => operation.Name == "AcceptanceSpecContentVersions");
        builder.Operations.OfType<CreateIndexOperation>()
            .Should().ContainSingle(operation =>
                operation.Table == "AcceptanceSpecContentVersions" &&
                operation.IsUnique &&
                operation.Columns.SequenceEqual(new[] { "AcceptanceSpecId", "Version" }));
        var baselineSql = builder.Operations.OfType<SqlOperation>()
            .Should().ContainSingle().Subject.Sql;
        baselineSql.Should().Contain("INSERT INTO `AcceptanceSpecContentVersions`")
            .And.Contain("`ReferenceVersion`")
            .And.Contain("migration-baseline")
            .And.NotContain("AcceptanceSpecReferenceEvents");
        DatabaseInitializer.ClassifyMigration(MigrationId)
            .Should().Be(DatabaseMigrationRisk.Safe);
    }

    [Fact]
    public void Model_ShouldEnforceVersionConcurrencyUniquenessAndCascadeDelete()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var db = new AppDbContext(options);
        var spec = db.Model.FindEntityType(typeof(AcceptanceSpec))!;
        var snapshot = db.Model.FindEntityType(typeof(AcceptanceSpecContentVersion))!;

        spec.FindProperty(nameof(AcceptanceSpec.ReferenceVersion))!
            .IsConcurrencyToken.Should().BeTrue();
        snapshot.GetIndexes().Should().ContainSingle(index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                new[]
                {
                    nameof(AcceptanceSpecContentVersion.AcceptanceSpecId),
                    nameof(AcceptanceSpecContentVersion.Version)
                }));
        snapshot.GetForeignKeys().Should().ContainSingle(foreignKey =>
            foreignKey.PrincipalEntityType == spec &&
            foreignKey.DeleteBehavior == DeleteBehavior.Cascade);
    }
}
