using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;
using System.Reflection;

namespace AcceptanceSpecSystem.Data.Tests;

public class UtcTimestampTests : TestBase
{
    [Fact]
    public void EntityDefaults_ShouldUseUtcNow()
    {
        new AcceptanceSpec().ImportedAt.Kind.Should().Be(DateTimeKind.Utc);
        new OrgCompany().CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        new OrgUnit().CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        new AuthRole().CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        new AuthUserOrgUnit().CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        new MatchingKnowledgeConfig().UpdatedAt.Kind.Should().Be(DateTimeKind.Utc);
        new PromptTemplate().CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        new AiServiceConfig().CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void AppDbContext_CurrentModel_ShouldNotExposeLegacyTextProcessingDbSets()
    {
        var propertyNames = typeof(Context.AppDbContext)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToList();

        propertyNames.Should().NotContain("SynonymGroups");
        propertyNames.Should().NotContain("SynonymWords");
        propertyNames.Should().NotContain("Keywords");
        propertyNames.Should().NotContain("TextProcessingConfigs");
    }

    [Fact]
    public void LegacyTableDropMigration_ShouldExist_AndDropLegacyTables()
    {
        var repositoryRoot = GetRepositoryRoot();
        var migrationsDirectory = Path.Combine(repositoryRoot, "src", "AcceptanceSpecSystem.Data", "Migrations");
        var migrationPath = Directory.GetFiles(migrationsDirectory, "*RemoveLegacyTextProcessingTables*.cs")
            .SingleOrDefault(path => !path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));

        migrationPath.Should().NotBeNull("需要单独的迁移删除 Synonym/Keyword/TextProcessing 旧表");

        var content = File.ReadAllText(migrationPath!);
        content.Should().Contain("DropTable(");
        content.Should().Contain("\"SynonymWords\"");
        content.Should().Contain("\"SynonymGroups\"");
        content.Should().Contain("\"Keywords\"");
        content.Should().Contain("\"TextProcessingConfigs\"");
        content.Should().Contain("INSERT INTO `MatchingKnowledgeConfigs`");
        content.Should().Contain("EntityAliasesJson");
        content.Should().Contain("UnitAliasesJson");
        content.Should().Contain("FieldAliasesJson");
        content.Should().Contain("FROM `SynonymWords`");
    }

    [Fact]
    public async Task PromptTemplateRepository_ShouldStampUtcTime_WhenCreatingAndUpdatingSystemTemplate()
    {
        var repository = new PromptTemplateRepository(Context);

        var created = await repository.GetOrCreateSystemAsync(
            PromptTemplateScene.MatchingReview,
            "matching-review",
            "智能填充复核",
            "default");
        created.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        await Context.SaveChangesAsync();

        created.DisplayName = string.Empty;
        created.Content = string.Empty;

        var updated = await repository.GetOrCreateSystemAsync(
            PromptTemplateScene.MatchingReview,
            "matching-review",
            "智能填充复核",
            "default");

        updated.UpdatedAt.Should().NotBeNull();
        updated.UpdatedAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AcceptanceSpecSystem.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("未找到仓库根目录");
    }
}
