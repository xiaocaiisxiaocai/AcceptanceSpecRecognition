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

    [Fact]
    public void Build_WhenMultipleNumericConstraintsExist_ShouldEvaluateEachSharedField()
    {
        var evidence = _builder.Build(
            new MatchSource
            {
                Project = "尺寸要求",
                Specification = "宽度小于0.5cm，高度等于1cm"
            },
            new MatchCandidate
            {
                SpecId = 5,
                Project = "尺寸要求",
                Specification = "宽度等于0.2cm，高度等于2cm"
            },
            MatchingKnowledge.CreateDefault());

        evidence.NumericConstraints.Should().HaveCount(2);
        evidence.NumericConstraints.Should().Contain(item =>
            item.FieldName == "宽度" && item.Relation == EvidenceRelation.Compatible);
        evidence.NumericConstraints.Should().Contain(item =>
            item.FieldName == "高度" && item.Relation == EvidenceRelation.Conflict);
        evidence.HasHardConflict.Should().BeTrue();
        evidence.Conflicts.Should().Contain(item => item.Contains("高度", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_WhenMultipleIdentifiersExist_ShouldCaptureRemainingIdentifierConflicts()
    {
        var evidence = _builder.Build(
            new MatchSource
            {
                Project = "设备 ABC-100 和 XYZ-200",
                Specification = "请使用 ABC-100 与 XYZ-200"
            },
            new MatchCandidate
            {
                SpecId = 6,
                Project = "设备 ABC-100 和 XYZ-201",
                Specification = "请使用 ABC-100 与 XYZ-201"
            },
            MatchingKnowledge.CreateDefault());

        evidence.Identifiers.Should().HaveCount(2);
        evidence.Identifiers.Should().Contain(item =>
            item.SourceValue == "ABC-100" &&
            item.CandidateValue == "ABC-100" &&
            item.Relation == EvidenceRelation.Exact);
        evidence.Identifiers.Should().Contain(item =>
            item.SourceValue == "XYZ-200" &&
            item.CandidateValue == "XYZ-201" &&
            item.Relation == EvidenceRelation.Conflict);
        evidence.HasHardConflict.Should().BeTrue();
    }

    [Fact]
    public void Build_WhenSemiconductorUnitsUseMicrometerSymbol_ShouldNormalizeAgainstNanometer()
    {
        var evidence = _builder.Build(
            new MatchSource
            {
                Project = "芯片工艺",
                Specification = "线宽等于0.13μm"
            },
            new MatchCandidate
            {
                SpecId = 7,
                Project = "芯片工艺",
                Specification = "线宽等于130nm"
            },
            MatchingKnowledge.CreateDefault());

        evidence.NumericConstraints.Should().ContainSingle();
        evidence.NumericConstraints[0].FieldName.Should().Be("线宽");
        evidence.NumericConstraints[0].Relation.Should().Be(EvidenceRelation.Exact);
        evidence.HasHardConflict.Should().BeFalse();
    }

    [Fact]
    public void Build_WhenBrandConfiguredOnceInLowercase_ShouldStillMatchUppercaseText()
    {
        var knowledge = new MatchingKnowledge
        {
            EntityAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["abb"] = "ABB"
            }
        };

        var evidence = _builder.Build(
            new MatchSource
            {
                Project = "ABB 控制柜",
                Specification = "品牌要求 ABB"
            },
            new MatchCandidate
            {
                SpecId = 8,
                Project = "abb 控制柜",
                Specification = "品牌要求 abb"
            },
            knowledge);

        evidence.Entities.Should().ContainSingle();
        evidence.Entities[0].NormalizedSourceValue.Should().Be("ABB");
        evidence.Entities[0].NormalizedCandidateValue.Should().Be("ABB");
        evidence.HasHardConflict.Should().BeFalse();
    }

    [Fact]
    public void CreateDefault_ShouldKeepOnlyMinimalStableConflictPairs()
    {
        var knowledge = MatchingKnowledge.CreateDefault();

        knowledge.ConflictPairs.Should().BeEquivalentTo(
        [
            ("输入", "输出"),
            ("投板", "收板"),
            ("上料", "下料"),
            ("正转", "反转"),
            ("loading", "unloading"),
            ("loader", "unloader")
        ]);
    }

    [Fact]
    public void CreateDefault_ShouldNotExposeConfigurableUnitFactors()
    {
        var knowledge = MatchingKnowledge.CreateDefault();

        knowledge.UnitFactors.Should().BeEmpty();
    }
}
