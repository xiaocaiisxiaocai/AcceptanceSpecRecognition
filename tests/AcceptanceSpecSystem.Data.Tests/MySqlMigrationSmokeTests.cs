using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AcceptanceSpecSystem.Data.Tests;

public class MySqlMigrationSmokeTests
{
    [MySqlSmokeFact]
    public async Task DatabaseMigrate_OnFreshIsolatedMySqlDatabase_ShouldApplyCurrentMigrationChain()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();

        await context.Database.MigrateAsync();

        var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var expectedMigrations = context.Database.GetMigrations().ToArray();

        appliedMigrations.Should().Equal(expectedMigrations);
        expectedMigrations.Should().NotBeEmpty();
        appliedMigrations.Should().EndWith(expectedMigrations[^1]);

        var promptTemplateLegacyColumn = await database.ExecuteScalarAsync("SHOW COLUMNS FROM PromptTemplates LIKE 'IsDefault';");
        promptTemplateLegacyColumn.Should().BeNull();
    }

    [MySqlSmokeFact]
    public async Task DatabaseMigrate_FromLegacySpecSetState_ShouldBackfillCustomerIdWithNativeMySqlSql()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var migrator = context.Database.GetService<IMigrator>();

        await migrator.MigrateAsync("20260113040000_AddWordFilePath");
        await SeedLegacySpecSetStateAsync(context, database);
        await migrator.MigrateAsync("20260113064729_RefactorSpecSetModel");

        var customerId = Convert.ToInt32(await database.ExecuteScalarAsync("""
            SELECT CustomerId
            FROM AcceptanceSpecs
            WHERE Project = 'legacy-project'
            LIMIT 1;
            """));
        var processCustomerColumn = await database.ExecuteScalarAsync("SHOW COLUMNS FROM Processes LIKE 'CustomerId';");

        customerId.Should().Be(Convert.ToInt32(await database.ExecuteScalarAsync("""
            SELECT Id
            FROM Customers
            WHERE Name = 'Legacy Customer'
            LIMIT 1;
            """)), "UPDATE ... JOIN 迁移应把 AcceptanceSpecs.CustomerId 回填为旧流程所属客户");
        processCustomerColumn.Should().BeNull("重构后 Processes.CustomerId 应被移除");
    }

    private static async Task SeedLegacySpecSetStateAsync(AppDbContext context, MySqlMigrationTestDatabase database)
    {
        var now = DateTime.UtcNow;

        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Customers (Name, CreatedAt) VALUES ({0}, {1});",
            "Legacy Customer",
            now);
        var customerId = Convert.ToInt32(await database.ExecuteScalarAsync("""
            SELECT Id
            FROM Customers
            WHERE Name = 'Legacy Customer'
            LIMIT 1;
            """));

        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Processes (CustomerId, Name, CreatedAt) VALUES ({0}, {1}, {2});",
            customerId,
            "Legacy Process",
            now);
        var processId = Convert.ToInt32(await database.ExecuteScalarAsync("""
            SELECT Id
            FROM Processes
            WHERE Name = 'Legacy Process'
            LIMIT 1;
            """));

        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO WordFiles (FileName, FileContent, FileHash, UploadedAt, FilePath) VALUES ({0}, {1}, {2}, {3}, {4});",
            "legacy.docx",
            new byte[] { 1, 2, 3 },
            "legacy-hash",
            now,
            "/tmp/legacy.docx");
        var wordFileId = Convert.ToInt32(await database.ExecuteScalarAsync("""
            SELECT Id
            FROM WordFiles
            WHERE FileName = 'legacy.docx'
            LIMIT 1;
            """));

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO AcceptanceSpecs (ProcessId, Project, Specification, Acceptance, Remark, WordFileId, ImportedAt)
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6});
            """,
            processId,
            "legacy-project",
            "legacy-specification",
            "legacy-acceptance",
            "legacy-remark",
            wordFileId,
            now);
    }
}
