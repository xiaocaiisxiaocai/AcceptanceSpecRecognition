using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

/// <summary>
/// Prompt 模板 Repository 测试。
/// </summary>
public class PromptTemplateRepositoryTests : TestBase
{
    [Fact]
    public async Task GetBySceneAsync_ShouldReturnSystemTemplateOnly()
    {
        // Arrange
        var repository = new PromptTemplateRepository(Context);
        Context.PromptTemplates.AddRange(
            new PromptTemplate
            {
                Name = "user-review",
                DisplayName = "用户模板",
                Content = "user",
                Scene = PromptTemplateScene.MatchingReview,
                IsSystem = false
            },
            new PromptTemplate
            {
                Name = "system-review",
                DisplayName = "系统模板",
                Content = "system",
                Scene = PromptTemplateScene.MatchingReview,
                IsSystem = true
            });
        await Context.SaveChangesAsync();

        // Act
        var result = await repository.GetBySceneAsync(PromptTemplateScene.MatchingReview);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("system-review");
    }

    [Fact]
    public async Task GetSystemTemplatesAsync_ShouldReturnSystemTemplatesOrderedByScene()
    {
        // Arrange
        var repository = new PromptTemplateRepository(Context);
        Context.PromptTemplates.AddRange(
            new PromptTemplate
            {
                Name = "user",
                DisplayName = "用户模板",
                Content = "user",
                Scene = PromptTemplateScene.ImportDuplicateReview,
                IsSystem = false
            },
            new PromptTemplate
            {
                Name = "rerank",
                DisplayName = "重排模板",
                Content = "rerank",
                Scene = PromptTemplateScene.MatchingCandidateRerank,
                IsSystem = true
            },
            new PromptTemplate
            {
                Name = "review",
                DisplayName = "复核模板",
                Content = "review",
                Scene = PromptTemplateScene.MatchingReview,
                IsSystem = true
            });
        await Context.SaveChangesAsync();

        // Act
        var result = await repository.GetSystemTemplatesAsync();

        // Assert
        result.Select(template => template.Name)
            .Should()
            .Equal("review", "rerank");
    }

    [Fact]
    public async Task GetOrCreateSystemAsync_ShouldCreateSystemTemplate_WhenMissing()
    {
        // Arrange
        var repository = new PromptTemplateRepository(Context);

        // Act
        var result = await repository.GetOrCreateSystemAsync(
            PromptTemplateScene.ImportDuplicateReview,
            "import-duplicate-review",
            "导入重复复核",
            "默认内容");
        await Context.SaveChangesAsync();

        // Assert
        result.Id.Should().BeGreaterThan(0);
        result.IsSystem.Should().BeTrue();
        result.Scene.Should().Be(PromptTemplateScene.ImportDuplicateReview);
        result.Name.Should().Be("import-duplicate-review");
        result.DisplayName.Should().Be("导入重复复核");
        result.Content.Should().Be("默认内容");
    }

    [Fact]
    public async Task GetOrCreateSystemAsync_ShouldRepairExistingTemplateMatchedByName()
    {
        // Arrange
        var repository = new PromptTemplateRepository(Context);
        var existing = new PromptTemplate
        {
            Name = "matching-review",
            DisplayName = "",
            Content = "",
            Scene = PromptTemplateScene.Unknown,
            IsSystem = false
        };
        Context.PromptTemplates.Add(existing);
        await Context.SaveChangesAsync();

        // Act
        var result = await repository.GetOrCreateSystemAsync(
            PromptTemplateScene.MatchingReview,
            "matching-review",
            "匹配复核",
            "默认内容");
        await Context.SaveChangesAsync();

        // Assert
        result.Id.Should().Be(existing.Id);
        result.IsSystem.Should().BeTrue();
        result.Scene.Should().Be(PromptTemplateScene.MatchingReview);
        result.DisplayName.Should().Be("匹配复核");
        result.Content.Should().Be("默认内容");
        result.UpdatedAt.Should().NotBeNull();
    }
}
