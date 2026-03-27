using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests;

public class MatchEvidenceBuilderTests
{
    private readonly MatchEvidenceBuilder _builder = new();

    [Fact]
    public void Build_WhenLessThanMatchesPointValue_ShouldProduceCompatibleNumericEvidence()
    {
        var evidence = _builder.Build(
            new MatchSource
            {
                Project = "尺寸要求",
                Specification = "宽度小于0.5cm"
            },
            new MatchCandidate
            {
                SpecId = 1,
                Project = "尺寸要求",
                Specification = "宽度等于0.2cm"
            },
            MatchingKnowledge.CreateDefault());

        evidence.NumericConstraints.Should().ContainSingle();
        evidence.NumericConstraints[0].FieldName.Should().Be("宽度");
        evidence.NumericConstraints[0].Relation.Should().Be(EvidenceRelation.Compatible);
        evidence.HasHardConflict.Should().BeFalse();
    }

    [Fact]
    public void Build_WhenBrandAliasMatches_ShouldProduceAliasSameEntityEvidence()
    {
        var evidence = _builder.Build(
            new MatchSource
            {
                Project = "Panasonic 设备",
                Specification = "品牌要求 Panasonic"
            },
            new MatchCandidate
            {
                SpecId = 2,
                Project = "松下 设备",
                Specification = "品牌要求 松下"
            },
            MatchingKnowledge.CreateDefault());

        evidence.Entities.Should().ContainSingle();
        evidence.Entities[0].Relation.Should().Be(EvidenceRelation.AliasSame);
        evidence.HasHardConflict.Should().BeFalse();
    }

    [Fact]
    public void Build_WhenIdentifierConflicts_ShouldMarkHardConflict()
    {
        var evidence = _builder.Build(
            new MatchSource
            {
                Project = "设备型号 ABC-100",
                Specification = "请使用 ABC-100"
            },
            new MatchCandidate
            {
                SpecId = 3,
                Project = "设备型号 ABC-700",
                Specification = "请使用 ABC-700"
            },
            MatchingKnowledge.CreateDefault());

        evidence.Identifiers.Should().ContainSingle();
        evidence.Identifiers[0].Relation.Should().Be(EvidenceRelation.Conflict);
        evidence.HasHardConflict.Should().BeTrue();
    }

    [Fact]
    public void Build_WhenCustomKnowledgeProvided_ShouldNotFallbackToDefaultKnowledge()
    {
        var knowledge = new MatchingKnowledge
        {
            EntityAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mitsubishi"] = "三菱",
                ["三菱"] = "三菱"
            }
        };

        var evidence = _builder.Build(
            new MatchSource
            {
                Project = "Mitsubishi 设备",
                Specification = "品牌要求 Mitsubishi"
            },
            new MatchCandidate
            {
                SpecId = 4,
                Project = "三菱 设备",
                Specification = "品牌要求 三菱"
            },
            knowledge);

        evidence.Entities.Should().ContainSingle();
        evidence.Entities[0].Relation.Should().Be(EvidenceRelation.AliasSame);
        evidence.HasHardConflict.Should().BeFalse();
    }
}
