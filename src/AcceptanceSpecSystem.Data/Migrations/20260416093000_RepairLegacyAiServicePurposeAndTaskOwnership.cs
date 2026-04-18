using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AcceptanceSpecSystem.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260416093000_RepairLegacyAiServicePurposeAndTaskOwnership")]
public partial class RepairLegacyAiServicePurposeAndTaskOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO AiServiceConfigs
                (Name, ServiceType, ApiKey, Endpoint, EmbeddingModel, LlmModel, CreatedAt, UpdatedAt, Priority, Purpose, DisableThinking, DefaultRecallTopK)
            SELECT
                CONCAT(LEFT(Name, 80), ' [Embedding #', Id, ']'),
                ServiceType,
                ApiKey,
                Endpoint,
                EmbeddingModel,
                NULL,
                CreatedAt,
                COALESCE(UpdatedAt, UTC_TIMESTAMP(6)),
                Priority,
                2,
                DisableThinking,
                DefaultRecallTopK
            FROM AiServiceConfigs
            WHERE Purpose = 3
              AND LlmModel IS NOT NULL
              AND LlmModel <> ''
              AND EmbeddingModel IS NOT NULL
              AND EmbeddingModel <> '';
            """);

        migrationBuilder.Sql(
            """
            UPDATE AiServiceConfigs
            SET Purpose = 1,
                EmbeddingModel = NULL,
                UpdatedAt = COALESCE(UpdatedAt, UTC_TIMESTAMP(6))
            WHERE Purpose = 3
              AND LlmModel IS NOT NULL
              AND LlmModel <> ''
              AND EmbeddingModel IS NOT NULL
              AND EmbeddingModel <> '';
            """);

        migrationBuilder.Sql(
            """
            UPDATE AiServiceConfigs
            SET Purpose = 1,
                EmbeddingModel = NULL,
                UpdatedAt = COALESCE(UpdatedAt, UTC_TIMESTAMP(6))
            WHERE Purpose = 3
              AND LlmModel IS NOT NULL
              AND LlmModel <> ''
              AND (EmbeddingModel IS NULL OR EmbeddingModel = '');
            """);

        migrationBuilder.Sql(
            """
            UPDATE AiServiceConfigs
            SET Purpose = 2,
                LlmModel = NULL,
                UpdatedAt = COALESCE(UpdatedAt, UTC_TIMESTAMP(6))
            WHERE Purpose = 3
              AND EmbeddingModel IS NOT NULL
              AND EmbeddingModel <> ''
              AND (LlmModel IS NULL OR LlmModel = '');
            """);

        migrationBuilder.Sql(
            """
            UPDATE AiServiceConfigs
            SET Purpose = 1,
                UpdatedAt = COALESCE(UpdatedAt, UTC_TIMESTAMP(6))
            WHERE Purpose = 3
              AND (LlmModel IS NULL OR LlmModel = '')
              AND (EmbeddingModel IS NULL OR EmbeddingModel = '');
            """);

        migrationBuilder.Sql(
            """
            UPDATE MatchingFillTasks AS task
            INNER JOIN WordFiles AS file ON file.Id = task.SourceFileId
            SET task.CreatedByUserId = COALESCE(task.CreatedByUserId, file.CreatedByUserId),
                task.CompanyId = COALESCE(task.CompanyId, file.CompanyId)
            WHERE (task.CreatedByUserId IS NULL OR task.CompanyId IS NULL)
              AND file.CreatedByUserId IS NOT NULL
              AND file.CompanyId IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
