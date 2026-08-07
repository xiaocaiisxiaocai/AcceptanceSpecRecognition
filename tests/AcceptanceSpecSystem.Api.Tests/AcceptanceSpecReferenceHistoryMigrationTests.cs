using System.Reflection;
using AcceptanceSpecSystem.Data;
using AcceptanceSpecSystem.Data.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class AcceptanceSpecReferenceHistoryMigrationTests
{
    private const string MigrationTypeName =
        "AcceptanceSpecSystem.Data.Migrations.AddAcceptanceSpecReferenceHistory";
    private const string MigrationId =
        "20260806065524_AddAcceptanceSpecReferenceHistory";

    [Fact]
    public void Migration_ShouldCreateVersionedHistoryAndUnknownTimeBaseline()
    {
        var migrationType = typeof(AppDbContext).Assembly.GetType(MigrationTypeName);
        migrationType.Should().NotBeNull();
        var migration = (Migration)Activator.CreateInstance(migrationType!)!;
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        migrationType!
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        builder.Operations.OfType<AddColumnOperation>()
            .Should().ContainSingle(operation =>
                operation.Table == "AcceptanceSpecs" &&
                operation.Name == "ReferenceVersion" &&
                Equals(operation.DefaultValue, 1L));
        builder.Operations.OfType<CreateTableOperation>()
            .Should().ContainSingle(operation =>
                operation.Name == "AcceptanceSpecReferenceEvents");
        var baselineSql = builder.Operations.OfType<SqlOperation>()
            .Should().ContainSingle().Subject.Sql;
        baselineSql.Should().Contain("ReferenceCount")
            .And.Contain("ReferencedAtUtc")
            .And.Contain("NULL")
            .And.Contain("WHERE `ReferenceCount` > 0");
        DatabaseInitializer.ClassifyMigration(MigrationId)
            .Should().Be(DatabaseMigrationRisk.Safe);
    }
}
