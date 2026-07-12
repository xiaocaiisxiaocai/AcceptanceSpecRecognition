using AcceptanceSpecSystem.Data.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

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
