using AcceptanceSpecSystem.Data.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Tests;

public sealed class DatabaseInitializerControlledMigrationTests
{
    [Fact]
    public void MigrationCatalog_ShouldClassifyKnownDataRewritesAsDestructive()
    {
        DatabaseInitializer.ClassifyMigration(DatabaseInitializer.ControlledCollationMigrationId)
            .Should().Be(DatabaseMigrationRisk.Destructive);
        DatabaseInitializer.ClassifyMigration("20260719170000_BackfillDocumentTemplateRegions")
            .Should().Be(DatabaseMigrationRisk.Destructive);
        DatabaseInitializer.ClassifyMigration("20260719074136_AddDocumentTemplateRegions")
            .Should().Be(DatabaseMigrationRisk.Safe);
        DatabaseInitializer.ClassifyMigration("20990101000000_UnreviewedMigration")
            .Should().Be(DatabaseMigrationRisk.Unclassified);
    }

    [Fact]
    public void UnclassifiedMigration_ShouldFailClosedOnExistingDatabase()
    {
        var action = () => DatabaseInitializer.EnsureControlledMigrationPolicy(
            ["20260719074136_AddDocumentTemplateRegions"],
            ["20990101000000_UnreviewedMigration"],
            allowControlledMigrations: false);

        action.Should().Throw<ControlledDatabaseMigrationRequiredException>()
            .WithMessage("*20990101000000_UnreviewedMigration*");
    }

    [Fact]
    public void ExistingDatabase_ShouldRejectControlledMigrationDuringNormalStartup()
    {
        var action = () => DatabaseInitializer.EnsureControlledMigrationPolicy(
            ["20260710144805_AddAuthRefreshSessions"],
            [DatabaseInitializer.ControlledCollationMigrationId],
            allowControlledMigrations: false);

        action.Should().Throw<ControlledDatabaseMigrationRequiredException>()
            .WithMessage("*--apply-destructive-migrations --backup-verified*");
    }

    [Fact]
    public void MigrateOnlyMode_ShouldAllowControlledMigration()
    {
        var action = () => DatabaseInitializer.EnsureControlledMigrationPolicy(
            ["20260710144805_AddAuthRefreshSessions"],
            [DatabaseInitializer.ControlledCollationMigrationId],
            allowControlledMigrations: true,
            backupVerified: true);

        action.Should().NotThrow();
    }

    [Fact]
    public void DestructiveMigration_ShouldRequireExplicitCommandAndVerifiedBackup()
    {
        var withoutBackup = () => DatabaseInitializer.EnsureControlledMigrationPolicy(
            ["20260710144805_AddAuthRefreshSessions"],
            [DatabaseInitializer.ControlledCollationMigrationId],
            allowControlledMigrations: true,
            backupVerified: false);

        withoutBackup.Should().Throw<ControlledDatabaseMigrationRequiredException>()
            .WithMessage("*--backup-verified*");

        var explicitlyApproved = () => DatabaseInitializer.EnsureControlledMigrationPolicy(
            ["20260710144805_AddAuthRefreshSessions"],
            [DatabaseInitializer.ControlledCollationMigrationId],
            allowControlledMigrations: true,
            backupVerified: true);

        explicitlyApproved.Should().NotThrow();
    }

    [Fact]
    public void SafeMigration_ShouldRemainAutomaticForExistingDatabase()
    {
        var action = () => DatabaseInitializer.EnsureControlledMigrationPolicy(
            ["20260719190000_AddColumnMappingRuleNormalizedUniqueKey"],
            ["20260719074136_AddDocumentTemplateRegions"],
            allowControlledMigrations: false,
            backupVerified: false);

        action.Should().NotThrow();
    }

    [Fact]
    public void FreshDatabase_ShouldRemainBootstrapCompatible()
    {
        var action = () => DatabaseInitializer.EnsureControlledMigrationPolicy(
            [],
            [DatabaseInitializer.ControlledCollationMigrationId],
            allowControlledMigrations: false);

        action.Should().NotThrow();
    }

    [Fact]
    public void ControlledMigrationMode_ShouldTemporarilyUseThirtyMinuteCommandTimeout()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(
                "Server=localhost;Database=timeout_test;User=test;Password=test;",
                new MySqlServerVersion(new Version(8, 0, 36)))
            .Options;
        using var context = new AppDbContext(options);
        context.Database.SetCommandTimeout(17);

        DatabaseInitializer.ControlledMigrationCommandTimeoutSeconds.Should().Be(1800);

        using (DatabaseInitializer.CreateControlledMigrationCommandTimeoutScope(
                   context,
                   allowControlledMigrations: true))
        {
            context.Database.GetCommandTimeout().Should().Be(1800);
        }

        context.Database.GetCommandTimeout().Should().Be(17);
    }

    [Fact]
    public void NormalStartup_ShouldNotChangeCommandTimeout()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(
                "Server=localhost;Database=timeout_test;User=test;Password=test;",
                new MySqlServerVersion(new Version(8, 0, 36)))
            .Options;
        using var context = new AppDbContext(options);
        context.Database.SetCommandTimeout(17);

        var commandTimeoutScope = DatabaseInitializer.CreateControlledMigrationCommandTimeoutScope(
            context,
            allowControlledMigrations: false);

        commandTimeoutScope.Should().BeNull();
        context.Database.GetCommandTimeout().Should().Be(17);
    }
}
