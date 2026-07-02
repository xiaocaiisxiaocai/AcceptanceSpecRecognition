using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

/// <summary>
/// 文档结构模板持久化测试。
/// </summary>
public class DocumentTemplateRepositoryTests : TestBase
{
    [Fact]
    public async Task SaveAndQuery_ShouldRoundTripStructureRecognitionFields()
    {
        var template = new DocumentTemplate
        {
            CustomerId = 1,
            TemplateName = "客户A-结构模板",
            HeadersFingerprint = "fp-001",
            HeadersJson = "[\"项目\",\"规格\"]",
            ProjectColumnIndex = null,
            SpecificationColumnIndex = 1,
            AcceptanceColumnIndex = 2,
            RemarkColumnIndex = null,
            HeaderRowIndex = 0,
            HeaderRowCount = 1,
            DataStartRowIndex = 1,
            DataEndRowIndex = null,
            IsSpecificationOnly = true,
            UsageCount = 1,
            LastUsedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        Context.Set<DocumentTemplate>().Add(template);
        await Context.SaveChangesAsync();

        var found = Context.Set<DocumentTemplate>()
            .Single(t => t.CustomerId == 1 && t.HeadersFingerprint == "fp-001");

        found.ProjectColumnIndex.Should().BeNull();
        found.SpecificationColumnIndex.Should().Be(1);
        found.DataEndRowIndex.Should().BeNull();
        found.IsSpecificationOnly.Should().BeTrue();
        found.UsageCount.Should().Be(1);
    }
}
