using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

public sealed class DatabaseInitializerControlledMigrationTests
{
    [Fact]
    public void ExistingDatabase_ShouldRejectControlledMigrationDuringNormalStartup()
    {
        var action = () => DatabaseInitializer.EnsureControlledMigrationPolicy(
            ["20260710144805_AddAuthRefreshSessions"],
            [DatabaseInitializer.ControlledCollationMigrationId],
            allowControlledMigrations: false);

        action.Should().Throw<ControlledDatabaseMigrationRequiredException>()
            .WithMessage("*--migrate-only*");
    }

    [Fact]
    public void MigrateOnlyMode_ShouldAllowControlledMigration()
    {
        var action = () => DatabaseInitializer.EnsureControlledMigrationPolicy(
            ["20260710144805_AddAuthRefreshSessions"],
            [DatabaseInitializer.ControlledCollationMigrationId],
            allowControlledMigrations: true);

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
}
