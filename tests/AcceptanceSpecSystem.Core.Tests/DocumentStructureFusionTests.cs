using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests;

public class DocumentStructureFusionTests
{
    [Fact]
    public void Merge_WhenLlmSuggestsMissingField_ShouldFillOnlyUnresolvedRuleField()
    {
        var rule = new DocumentStructureCandidate
        {
            TableIndex = 0,
            ProjectColumnIndex = 0,
            SpecificationColumnIndex = null,
            AcceptanceColumnIndex = 2,
            RemarkColumnIndex = null,
            Confidence = 0.72,
            Source = DocumentStructureCandidateSource.Rule
        };
        var llm = new DocumentStructureCandidate
        {
            TableIndex = 0,
            ProjectColumnIndex = 3,
            SpecificationColumnIndex = 1,
            AcceptanceColumnIndex = 4,
            RemarkColumnIndex = 5,
            Confidence = 0.86,
            Source = DocumentStructureCandidateSource.Llm
        };

        var merged = DocumentStructureFusion.Merge(rule, llm);

        merged.ProjectColumnIndex.Should().Be(0);
        merged.SpecificationColumnIndex.Should().Be(1);
        merged.AcceptanceColumnIndex.Should().Be(2);
        merged.RemarkColumnIndex.Should().Be(5);
        merged.Source.Should().Be(DocumentStructureCandidateSource.Fused);
    }

    [Fact]
    public void Merge_WhenRuleFieldIsHighConfidence_ShouldNotLetLlmOverrideIt()
    {
        var rule = new DocumentStructureCandidate
        {
            TableIndex = 0,
            ProjectColumnIndex = 0,
            SpecificationColumnIndex = 1,
            AcceptanceColumnIndex = 2,
            Confidence = 0.93,
            Source = DocumentStructureCandidateSource.Rule
        };
        var llm = new DocumentStructureCandidate
        {
            TableIndex = 0,
            ProjectColumnIndex = 2,
            SpecificationColumnIndex = 3,
            AcceptanceColumnIndex = 4,
            Confidence = 0.91,
            Source = DocumentStructureCandidateSource.Llm
        };

        var merged = DocumentStructureFusion.Merge(rule, llm);

        merged.ProjectColumnIndex.Should().Be(0);
        merged.SpecificationColumnIndex.Should().Be(1);
        merged.AcceptanceColumnIndex.Should().Be(2);
    }

    [Fact]
    public void Merge_WhenLlmOverrideIsAllowed_ShouldUseLlmColumnsAsAdjudication()
    {
        var rule = new DocumentStructureCandidate
        {
            TableIndex = 0,
            ProjectColumnIndex = 0,
            SpecificationColumnIndex = 2,
            AcceptanceColumnIndex = 2,
            RemarkColumnIndex = null,
            Confidence = 0.9,
            Source = DocumentStructureCandidateSource.Rule
        };
        var llm = new DocumentStructureCandidate
        {
            TableIndex = 0,
            ProjectColumnIndex = 0,
            SpecificationColumnIndex = 1,
            AcceptanceColumnIndex = 2,
            RemarkColumnIndex = 3,
            Confidence = 0.92,
            Source = DocumentStructureCandidateSource.Llm
        };

        var merged = DocumentStructureFusion.Merge(rule, llm, allowLlmOverride: true);

        merged.ProjectColumnIndex.Should().Be(0);
        merged.SpecificationColumnIndex.Should().Be(1);
        merged.AcceptanceColumnIndex.Should().Be(2);
        merged.RemarkColumnIndex.Should().Be(3);
        merged.Source.Should().Be(DocumentStructureCandidateSource.Fused);
    }

    [Fact]
    public void Merge_WhenLlmResultBelongsToDifferentTable_ShouldIgnoreLlm()
    {
        var rule = new DocumentStructureCandidate
        {
            TableIndex = 0,
            SpecificationColumnIndex = null,
            Confidence = 0.5,
            Source = DocumentStructureCandidateSource.Rule
        };
        var llm = new DocumentStructureCandidate
        {
            TableIndex = 1,
            SpecificationColumnIndex = 1,
            Confidence = 0.95,
            Source = DocumentStructureCandidateSource.Llm
        };

        var merged = DocumentStructureFusion.Merge(rule, llm);

        merged.SpecificationColumnIndex.Should().BeNull();
        merged.Source.Should().Be(DocumentStructureCandidateSource.Rule);
    }
}
