using System.Reflection;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Migrations;
using AcceptanceSpecSystem.Data.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AcceptanceSpecSystem.Data.Tests;

public sealed class DocumentTemplateUniqueIndexMigrationTests
{
    private const string PreviousMigrationId = "20260727090000_AddWordFilePendingDeletion";
    private const string MigrationId = "20260728090000_RestoreDocumentTemplateFingerprintUniqueIndex";
    private const string IndexName = "IX_DocumentTemplates_CustomerId_HeadersFingerprint";

    [Fact]
    public void Migration_ShouldReplaceFingerprintIndexInSingleAlterAndRemainControlled()
    {
        var migration = new RestoreDocumentTemplateFingerprintUniqueIndex();
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        typeof(RestoreDocumentTemplateFingerprintUniqueIndex)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var sql = builder.Operations.OfType<SqlOperation>().Should().ContainSingle().Subject.Sql;
        sql.Should().Contain("ALTER TABLE `DocumentTemplates`")
            .And.Contain($"DROP INDEX `{IndexName}`")
            .And.Contain($"ADD UNIQUE INDEX `{IndexName}`");
        DatabaseInitializer.ClassifyMigration(MigrationId).Should().Be(DatabaseMigrationRisk.Destructive);
    }

    [MySqlSmokeFact]
    public async Task Migration_ShouldRepairLegacyNonUniqueIndexInRealMySql()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigrationId);
        await context.Database.ExecuteSqlRawAsync(
            $"""
            ALTER TABLE `DocumentTemplates`
                DROP INDEX `{IndexName}`,
                ADD INDEX `{IndexName}` (`CustomerId`, `HeadersFingerprint`);
            """);

        await migrator.MigrateAsync(MigrationId);

        Convert.ToInt32(await database.ExecuteScalarAsync(
            $"""
            SELECT NON_UNIQUE
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'DocumentTemplates'
              AND INDEX_NAME = '{IndexName}'
            LIMIT 1;
            """)).Should().Be(0);
    }

    [MySqlSmokeFact]
    public async Task Migration_ShouldSucceedWhenFingerprintIndexIsAlreadyUnique()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigrationId);

        await migrator.MigrateAsync(MigrationId);

        Convert.ToInt32(await database.ExecuteScalarAsync(
            $"""
            SELECT NON_UNIQUE
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'DocumentTemplates'
              AND INDEX_NAME = '{IndexName}'
            LIMIT 1;
            """)).Should().Be(0);
    }

    [MySqlSmokeFact]
    public async Task Migration_WithDuplicateFingerprints_ShouldFailWithoutChangingDataOrIndexAndRemainRetryable()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigrationId);
        await context.Database.ExecuteSqlRawAsync(
            $"""
            ALTER TABLE `DocumentTemplates`
                DROP INDEX `{IndexName}`,
                ADD INDEX `{IndexName}` (`CustomerId`, `HeadersFingerprint`);
            """);
        var customer = new Customer { Name = $"迁移重复模板客户-{Guid.NewGuid():N}" };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();
        var now = DateTime.UtcNow;
        static DocumentTemplate Template(int customerId, string name, DateTime now) => new()
        {
            CustomerId = customerId,
            TemplateName = name,
            HeadersFingerprint = new string('d', 64),
            HeadersJson = "[\"项目\",\"规格\"]",
            SpecificationColumnIndex = 1,
            HeaderRowIndex = 0,
            HeaderRowCount = 1,
            DataStartRowIndex = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        context.DocumentTemplates.AddRange(
            Template(customer.Id, "重复模板1", now),
            Template(customer.Id, "重复模板2", now));
        await context.SaveChangesAsync();

        var migrate = async () => await migrator.MigrateAsync(MigrationId);
        await migrate.Should().ThrowAsync<Exception>();
        Convert.ToInt32(await database.ExecuteScalarAsync(
            "SELECT COUNT(*) FROM DocumentTemplates WHERE CustomerId = " + customer.Id + ";"))
            .Should().Be(2);
        Convert.ToInt32(await database.ExecuteScalarAsync(
            $"""
            SELECT NON_UNIQUE
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'DocumentTemplates'
              AND INDEX_NAME = '{IndexName}'
            LIMIT 1;
            """)).Should().Be(1);
        Convert.ToInt32(await database.ExecuteScalarAsync(
            $"SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '{MigrationId}';"))
            .Should().Be(0);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM DocumentTemplates
            WHERE CustomerId = {customer.Id}
              AND Id <> (
                  SELECT Id FROM (
                      SELECT MIN(Id) AS Id
                      FROM DocumentTemplates
                      WHERE CustomerId = {customer.Id}
                  ) survivor
              );
            """);
        await migrator.MigrateAsync(MigrationId);
        Convert.ToInt32(await database.ExecuteScalarAsync(
            $"""
            SELECT NON_UNIQUE
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'DocumentTemplates'
              AND INDEX_NAME = '{IndexName}'
            LIMIT 1;
            """)).Should().Be(0);
    }
}
