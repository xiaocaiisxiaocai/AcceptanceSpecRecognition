using System.Reflection;
using AcceptanceSpecSystem.Data;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class AcceptanceSpecCleanupMigrationTests
{
    private const string MigrationTypeName =
        "AcceptanceSpecSystem.Data.Migrations.AddAcceptanceSpecCleanupScanner";
    private const string MigrationId = "20260811064828_AddAcceptanceSpecCleanupScanner";

    [Fact]
    public void Migration_ShouldOnlyAddActiveDefaultsAndCleanupTables()
    {
        var migrationType = typeof(AppDbContext).Assembly.GetType(MigrationTypeName);
        migrationType.Should().NotBeNull();
        var migration = (Migration)Activator.CreateInstance(migrationType!)!;
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        migrationType!.GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        builder.Operations.OfType<AddColumnOperation>().Should().Contain(operation =>
            operation.Table == "AcceptanceSpecs" && operation.Name == "CleanupStatus" &&
            Equals(operation.DefaultValue, (int)AcceptanceSpecCleanupStatus.Active));
        builder.Operations.OfType<CreateTableOperation>().Select(operation => operation.Name)
            .Should().Contain(new[]
            {
                "AcceptanceSpecCleanupScans",
                "AcceptanceSpecCleanupScanItems",
                "AcceptanceSpecCleanupDeletionRecords"
            });
        builder.Operations.Should().NotContain(operation => operation is DeleteDataOperation);
        builder.Operations.Should().NotContain(operation => operation is DropTableOperation);
        DatabaseInitializer.ClassifyMigration(MigrationId).Should().Be(DatabaseMigrationRisk.Safe);
    }

    [Fact]
    public void Model_ShouldFilterActiveSpecsAndCascadeCleanupOwnedData()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:").Options;
        using var db = new AppDbContext(options);
        var spec = db.Model.FindEntityType(typeof(AcceptanceSpec))!;
        var scanItem = db.Model.FindEntityType(typeof(AcceptanceSpecCleanupScanItem))!;

        spec.GetQueryFilter().Should().NotBeNull();
        scanItem.GetIndexes().Should().Contain(index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                new[]
                {
                    nameof(AcceptanceSpecCleanupScanItem.ScanId),
                    nameof(AcceptanceSpecCleanupScanItem.AcceptanceSpecId)
                }));
        scanItem.GetForeignKeys().Should().OnlyContain(key => key.DeleteBehavior == DeleteBehavior.Cascade);
    }
}
