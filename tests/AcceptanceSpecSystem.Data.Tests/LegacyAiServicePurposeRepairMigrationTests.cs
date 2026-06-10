using System.Reflection;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Migrations;
using AcceptanceSpecSystem.Data.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AcceptanceSpecSystem.Data.Tests;

public class LegacyAiServicePurposeRepairMigrationTests
{
    private const string PreviousMigrationId = "20260415084722_RemovePromptTemplateIsDefault";
    private const string MigrationId = "20260416093000_RepairLegacyAiServicePurposeAndTaskOwnership";

    [Fact]
    public void RepairMigration_ShouldSplitTrueDualPurposeRowsAndOnlyFallbackMissingModels()
    {
        var migration = new RepairLegacyAiServicePurposeAndTaskOwnership();
        var migrationBuilder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");

        typeof(RepairLegacyAiServicePurposeAndTaskOwnership)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, new object[] { migrationBuilder });

        var sqlOperations = migrationBuilder.Operations.OfType<SqlOperation>().ToArray();

        sqlOperations.Should().HaveCount(6, "当前修复迁移应先拆分真实双用途，再归一单模型脏数据，并补齐任务归属");
        sqlOperations[0].Sql.Should().Contain("INSERT INTO AiServiceConfigs");
        sqlOperations[0].Sql.Should().Contain("CONCAT(LEFT(Name, 80), ' [Embedding #', Id, ']')");
        sqlOperations[1].Sql.Should().Contain("SET Purpose = 1");
        sqlOperations[1].Sql.Should().Contain("EmbeddingModel = NULL",
            "拆分后原始记录应收敛为单一 LLM 配置，避免继续保留隐藏的 Embedding 模型");
        sqlOperations[4].Sql.Should().Contain("WHERE Purpose = 3");
        sqlOperations[4].Sql.Should().Contain("(LlmModel IS NULL OR LlmModel = '')");
        sqlOperations[4].Sql.Should().Contain("(EmbeddingModel IS NULL OR EmbeddingModel = '')",
            "只有真正缺模型信息的脏数据才允许兜底回落");
    }

    [Fact]
    public void RepairMigration_ShouldBackfillTaskOwnershipWithoutOverwritingExistingValues()
    {
        var migration = new RepairLegacyAiServicePurposeAndTaskOwnership();
        var migrationBuilder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");

        typeof(RepairLegacyAiServicePurposeAndTaskOwnership)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, new object[] { migrationBuilder });

        var sqlOperations = migrationBuilder.Operations.OfType<SqlOperation>().ToArray();

        sqlOperations[5].Sql.Should().Contain("SET task.CreatedByUserId = COALESCE(task.CreatedByUserId, file.CreatedByUserId)");
        sqlOperations[5].Sql.Should().Contain("task.CompanyId = COALESCE(task.CompanyId, file.CompanyId)",
            "任务归属修复只应补齐空字段，不能改写任务原本已有的创建人或公司归属");
    }

    [MySqlSmokeFact]
    public async Task RepairMigration_OnLegacyAiServiceConfigs_ShouldSplitDualPurposeAndNormalizeSinglePurposeRows()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var migrator = context.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigrationId);
        await SeedLegacyAiServiceConfigsAsync(context);

        await migrator.MigrateAsync(MigrationId);

        var llmRows = await database.QueryAsync("""
            SELECT Purpose, LlmModel, EmbeddingModel
            FROM AiServiceConfigs
            WHERE Name = 'dual-purpose'
            LIMIT 1;
            """);
        llmRows.Rows.Count.Should().Be(1);
        llmRows.Rows[0]["Purpose"].Should().Be(1);
        llmRows.Rows[0]["LlmModel"].Should().Be("gpt-4.1");
        llmRows.Rows[0]["EmbeddingModel"].Should().Be(DBNull.Value,
            "拆分后原始双用途记录不应再保留隐藏的 Embedding 模型");

        var embeddingRows = await database.QueryAsync("""
            SELECT Name, Purpose, LlmModel, EmbeddingModel
            FROM AiServiceConfigs
            WHERE Purpose = 2
              AND EmbeddingModel = 'text-embedding-3-large';
            """);
        embeddingRows.Rows.Count.Should().Be(1);
        embeddingRows.Rows[0]["Name"].ToString().Should().StartWith("dual-purpose [Embedding #");
        embeddingRows.Rows[0]["LlmModel"].Should().Be(DBNull.Value,
            "拆分出来的 Embedding 记录不应继续保留 LLM 模型");

        (await ReadPurposeAsync(database, "llm-only")).Should().Be(1);
        (await ReadPurposeAsync(database, "embedding-only")).Should().Be(2);
        (await ReadPurposeAsync(database, "missing-models")).Should().Be(1,
            "完全缺少模型信息的历史脏数据仍需要兜底回落到 LLM");
    }

    [MySqlSmokeFact]
    public async Task RepairMigration_OnLegacyMatchingFillTasks_ShouldBackfillOnlyMissingOwnershipFields()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var migrator = context.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigrationId);
        await SeedLegacyMatchingFillTasksAsync(context, database);

        await migrator.MigrateAsync(MigrationId);

        var repairedTask = await database.QueryAsync("""
            SELECT CreatedByUserId, CompanyId
            FROM MatchingFillTasks
            WHERE TaskId = 'missing-owner'
            LIMIT 1;
            """);
        repairedTask.Rows.Count.Should().Be(1);
        repairedTask.Rows[0]["CreatedByUserId"].Should().Be(41);
        repairedTask.Rows[0]["CompanyId"].Should().Be(9);

        var partialTask = await database.QueryAsync("""
            SELECT CreatedByUserId, CompanyId
            FROM MatchingFillTasks
            WHERE TaskId = 'partial-owner'
            LIMIT 1;
            """);
        partialTask.Rows.Count.Should().Be(1);
        partialTask.Rows[0]["CreatedByUserId"].Should().Be(77,
            "迁移只应补齐空字段，不应覆盖任务上已存在的创建人");
        partialTask.Rows[0]["CompanyId"].Should().Be(9);
    }

    private static async Task SeedLegacyAiServiceConfigsAsync(AppDbContext context)
    {
        var now = DateTime.UtcNow;

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO AiServiceConfigs
                (Name, ServiceType, ApiKey, Endpoint, EmbeddingModel, LlmModel, CreatedAt, UpdatedAt, Priority, Purpose, DisableThinking, DefaultRecallTopK)
            VALUES
                ({0}, {1}, NULL, NULL, {2}, {3}, {4}, NULL, 0, 3, 0, 2),
                ({5}, {6}, NULL, NULL, {7}, {8}, {9}, NULL, 0, 3, 0, 2),
                ({10}, {11}, NULL, NULL, {12}, {13}, {14}, NULL, 0, 3, 0, 2),
                ({15}, {16}, NULL, NULL, {17}, {18}, {19}, NULL, 0, 3, 0, 2);
            """,
            "dual-purpose",
            0,
            "text-embedding-3-large",
            "gpt-4.1",
            now,
            "llm-only",
            0,
            "",
            "gpt-4.1-mini",
            now,
            "embedding-only",
            0,
            "text-embedding-3-small",
            "",
            now,
            "missing-models",
            0,
            "",
            "",
            now);
    }

    private static async Task SeedLegacyMatchingFillTasksAsync(AppDbContext context, MySqlMigrationTestDatabase database)
    {
        var now = DateTime.UtcNow;

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO WordFiles
                (FileName, FileContent, FileHash, UploadedAt, FilePath, FileType, CreatedByUserId, CompanyId, OwnerOrgUnitId)
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, NULL);
            """,
            "legacy-source.docx",
            new byte[] { 1, 2, 3 },
            "legacy-source-hash",
            now,
            "/tmp/legacy-source.docx",
            0,
            41,
            9);

        var sourceFileId = Convert.ToInt32(await database.ExecuteScalarAsync("""
            SELECT Id
            FROM WordFiles
            WHERE FileHash = 'legacy-source-hash'
            LIMIT 1;
            """));

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO MatchingFillTasks
                (TaskId, SourceFileId, CreatedByUserId, CompanyId, PayloadJson, CreatedAt)
            VALUES
                ({0}, {1}, NULL, NULL, {2}, {3}),
                ({4}, {5}, {6}, NULL, {7}, {8});
            """,
            "missing-owner",
            sourceFileId,
            "{}",
            now,
            "partial-owner",
            sourceFileId,
            77,
            "{}",
            now);
    }

    private static async Task<int> ReadPurposeAsync(MySqlMigrationTestDatabase database, string name)
    {
        return Convert.ToInt32(await database.ExecuteScalarAsync(
            $"""
             SELECT Purpose
             FROM AiServiceConfigs
             WHERE Name = '{name}'
             LIMIT 1;
             """));
    }
}
