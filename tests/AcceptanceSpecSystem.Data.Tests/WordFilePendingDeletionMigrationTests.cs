using System.Reflection;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Migrations;
using AcceptanceSpecSystem.Data.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AcceptanceSpecSystem.Data.Tests;

public class WordFilePendingDeletionMigrationTests
{
    private const string PreviousMigrationId = "20260726163227_RestrictAcceptanceSpecReferenceDeletes";
    private const string MigrationId = "20260727090000_AddWordFilePendingDeletion";

    [Fact]
    public void WordFile删除状态模型_应包含默认值过滤器复合索引和限制删除()
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"word-file-deletion-model-{Guid.NewGuid():N}")
                .Options);

        var entity = context.Model.FindEntityType(typeof(WordFile))!;
        entity.FindProperty(nameof(WordFile.DeletionStatus))!.GetDefaultValue()
            .Should().Be(WordFileDeletionStatus.Active);
        entity.FindProperty(nameof(WordFile.DeletionRetryCount))!.GetDefaultValue().Should().Be(0);
        entity.GetQueryFilter().Should().NotBeNull();
        entity.GetIndexes().Should().Contain(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
                new[] { nameof(WordFile.DeletionStatus), nameof(WordFile.NextDeletionAttemptAt), nameof(WordFile.Id) }));

        var matchingForeignKey = context.Model.FindEntityType(typeof(MatchingFillTask))!
            .GetForeignKeys()
            .Single(key => key.PrincipalEntityType.ClrType == typeof(WordFile));
        matchingForeignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
    }

    [Fact]
    public void 待删除文件_普通查询不可见而显式清理查询可见()
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"word-file-deletion-filter-{Guid.NewGuid():N}")
                .Options);
        context.WordFiles.Add(new WordFile
        {
            FileName = "pending.docx",
            FileHash = "hash",
            DeletionStatus = WordFileDeletionStatus.PendingDeletion
        });
        context.SaveChanges();

        context.WordFiles.Should().BeEmpty();
        context.WordFiles.IgnoreQueryFilters().Should().ContainSingle();
    }

    [Fact]
    public void 删除状态迁移_应包含默认值索引限制删除且可回滚()
    {
        var migration = new AddWordFilePendingDeletion();
        var up = BuildOperations(migration, "Up");
        up.OfType<AddColumnOperation>()
            .Single(operation => operation.Name == "DeletionStatus")
            .DefaultValue.Should().Be(0);
        up.OfType<AddColumnOperation>()
            .Single(operation => operation.Name == "DeletionRetryCount")
            .DefaultValue.Should().Be(0);
        up.OfType<CreateIndexOperation>().Should().Contain(operation =>
            operation.Name == "IX_WordFiles_DeletionStatus_NextDeletionAttemptAt_Id" &&
            operation.Columns.SequenceEqual(new[] { "DeletionStatus", "NextDeletionAttemptAt", "Id" }));
        up.OfType<CreateIndexOperation>().Should().Contain(operation =>
            operation.Name == "IX_DocumentImportExecutions_SourceFileId");
        up.OfType<AddForeignKeyOperation>()
            .Single(operation => operation.Name == "FK_MatchingFillTasks_WordFiles_SourceFileId")
            .OnDelete.Should().Be(ReferentialAction.Restrict);

        var down = BuildOperations(migration, "Down");
        down.OfType<DropIndexOperation>().Should().Contain(operation =>
            operation.Name == "IX_WordFiles_DeletionStatus_NextDeletionAttemptAt_Id");
        down.OfType<DropColumnOperation>().Select(operation => operation.Name)
            .Should().Contain(new[] { "DeletionStatus", "DeletionRetryCount", "DeletionLeaseToken" });
    }

    [Fact]
    public void 删除状态迁移_固定标识应唯一且分类为安全()
    {
        typeof(AddWordFilePendingDeletion)
            .GetCustomAttribute<MigrationAttribute>()!.Id.Should().Be(MigrationId);
        DatabaseInitializer.ClassifyMigration(MigrationId).Should().Be(DatabaseMigrationRisk.Safe);
    }

    [MySqlSmokeFact]
    public async Task 删除状态迁移_真实MySQL应支持升级默认值索引和回滚()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigrationId);

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO WordFiles (FileName, FileContent, FileHash, UploadedAt, FileType)
            VALUES ('legacy.docx', 0x01, 'legacy-pending-default', UTC_TIMESTAMP(6), 0);
            """);

        await migrator.MigrateAsync(MigrationId);
        var defaults = await database.QueryAsync(
            "SELECT DeletionStatus, DeletionRetryCount FROM WordFiles WHERE FileHash = 'legacy-pending-default';");
        defaults.Rows[0]["DeletionStatus"].Should().Be(0);
        defaults.Rows[0]["DeletionRetryCount"].Should().Be(0);
        Convert.ToInt32(await database.ExecuteScalarAsync(
            """
            SELECT COUNT(*) FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'WordFiles'
              AND INDEX_NAME = 'IX_WordFiles_DeletionStatus_NextDeletionAttemptAt_Id';
            """)).Should().Be(3);

        await migrator.MigrateAsync(PreviousMigrationId);
        Convert.ToInt32(await database.ExecuteScalarAsync(
            """
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'WordFiles'
              AND COLUMN_NAME = 'DeletionStatus';
            """)).Should().Be(0);

        Convert.ToInt32(await database.ExecuteScalarAsync(
            "SELECT COUNT(*) FROM WordFiles WHERE FileHash = 'legacy-pending-default';"))
            .Should().Be(1);

        await migrator.MigrateAsync(MigrationId);
        var reupgradedDefaults = await database.QueryAsync(
            "SELECT DeletionStatus, DeletionRetryCount FROM WordFiles WHERE FileHash = 'legacy-pending-default';");
        reupgradedDefaults.Rows.Count.Should().Be(1);
        reupgradedDefaults.Rows[0]["DeletionStatus"].Should().Be(0);
        reupgradedDefaults.Rows[0]["DeletionRetryCount"].Should().Be(0);
        Convert.ToInt32(await database.ExecuteScalarAsync(
            """
            SELECT COUNT(*) FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'WordFiles'
              AND INDEX_NAME = 'IX_WordFiles_DeletionStatus_NextDeletionAttemptAt_Id';
            """)).Should().Be(3);
    }

    private static IReadOnlyList<MigrationOperation> BuildOperations(Migration migration, string methodName)
    {
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        migration.GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, new object[] { builder });
        return builder.Operations;
    }
}
