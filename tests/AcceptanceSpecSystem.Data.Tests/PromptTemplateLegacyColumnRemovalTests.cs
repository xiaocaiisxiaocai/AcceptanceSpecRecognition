using System.Reflection;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Tests;

public class PromptTemplateLegacyColumnRemovalTests : TestBase
{
    [Fact]
    public void PromptTemplateEntity_ShouldNotExposeLegacyIsDefaultProperty()
    {
        typeof(PromptTemplate)
            .GetProperty("IsDefault", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Should()
            .BeNull("PromptTemplate 实体不应再保留历史 IsDefault 字段");
    }

    [Fact]
    public void AppDbContext_CurrentModel_ShouldNotExposePromptTemplateIsDefaultProperty()
    {
        var entityType = Context.Model.FindEntityType(typeof(PromptTemplate));

        entityType.Should().NotBeNull();
        entityType!
            .GetProperties()
            .Select(property => property.Name)
            .Should()
            .NotContain("IsDefault", "当前 EF 模型不应再把 PromptTemplate.IsDefault 当作映射列");
    }

    [Fact]
    public void CurrentModelArtifacts_ShouldNotContainPromptTemplateIsDefaultProperty()
    {
        var repositoryRoot = TestPathHelper.GetRepositoryRoot();
        var files = new[]
        {
            Path.Combine(repositoryRoot, "src", "AcceptanceSpecSystem.Data", "Migrations", "AppDbContextModelSnapshot.cs"),
            Path.Combine(repositoryRoot, "src", "AcceptanceSpecSystem.Data", "Migrations", "20260415084722_RemovePromptTemplateIsDefault.Designer.cs")
        };

        foreach (var file in files)
        {
            File.ReadAllText(file)
                .Should()
                .NotContain("b.Property<bool>(\"IsDefault\")", $"{Path.GetFileName(file)} 不应把 IsDefault 作为当前模型属性保留下来");
        }
    }

    [Fact]
    public void PromptTemplateIsDefaultRemovalMigration_ShouldOnlyDropLegacyColumn()
    {
        var migrationPath = Path.Combine(
            TestPathHelper.GetRepositoryRoot(),
            "src",
            "AcceptanceSpecSystem.Data",
            "Migrations",
            "20260415084722_RemovePromptTemplateIsDefault.cs");

        var content = File.ReadAllText(migrationPath);

        content.Should().Contain("DropColumn(");
        content.Should().Contain("name: \"IsDefault\"");
        content.Should().Contain("table: \"PromptTemplates\"");
        content.Should().Contain("AddColumn<bool>(");
        content.Should().NotContain("UpdateData(", "删除历史列的迁移不应继续维护默认模板数据语义");
        content.Should().NotContain("Sql(", "删除历史列的迁移不应依赖额外 SQL 去保留旧语义");
    }

    [MySqlSmokeFact]
    public async Task MigratedMySqlSchema_ShouldNotContainPromptTemplateLegacyIsDefaultColumn()
    {
        await using var database = await MySqlMigrationTestDatabase.CreateAsync();
        await using var migrationContext = database.CreateDbContext();

        await migrationContext.Database.MigrateAsync();

        var columnName = await database.ExecuteScalarAsync("SHOW COLUMNS FROM PromptTemplates LIKE 'IsDefault';");

        columnName.Should().BeNull("真实迁移完成后不应再保留 PromptTemplates.IsDefault 历史列");
    }
}
