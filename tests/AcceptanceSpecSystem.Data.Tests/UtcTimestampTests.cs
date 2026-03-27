using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

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
        new TextProcessingConfig().UpdatedAt.Kind.Should().Be(DateTimeKind.Utc);
        new PromptTemplate().CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        new AiServiceConfig().CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
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
}
