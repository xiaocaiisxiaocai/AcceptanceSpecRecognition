using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests;

public class MatchEvidenceBuilderTests
{
    private readonly MatchEvidenceBuilder _builder = new();

    [Fact]
    public void MatchEvidenceBuilder_PublicSignatures_ShouldNotRequireMatchingKnowledge()
    {
        typeof(IMatchEvidenceBuilder)
            .GetMethod(nameof(IMatchEvidenceBuilder.Build))!
            .GetParameters()
            .Select(parameter => parameter.ParameterType.Name)
            .Should()
            .Equal("MatchSource", "MatchCandidate");

        typeof(MatchEvidenceBuilder)
            .GetMethod(nameof(MatchEvidenceBuilder.Build))!
            .GetParameters()
            .Select(parameter => parameter.ParameterType.Name)
            .Should()
            .Equal("MatchSource", "MatchCandidate");
    }

    [Fact]
    public void MatchingKnowledge_AndProviderTypes_ShouldBeRemovedFromCoreAssembly()
    {
        var assembly = typeof(MatchEvidenceBuilder).Assembly;

        assembly.GetType("AcceptanceSpecSystem.Core.Matching.Models.MatchingKnowledge")
            .Should()
            .BeNull();
        assembly.GetType("AcceptanceSpecSystem.Core.Matching.Interfaces.IMatchingKnowledgeProvider")
            .Should()
            .BeNull();
    }

    [Fact]
    public void MatchingKnowledge_PlaceholderFiles_ShouldBeDeleted()
    {
        var repositoryRoot = GetRepositoryRoot();

        File.Exists(Path.Combine(
                repositoryRoot,
                "src/AcceptanceSpecSystem.Core/Matching/Models/MatchingKnowledgeModels.cs"
                    .Replace('/', Path.DirectorySeparatorChar)))
            .Should()
            .BeFalse("matching knowledge 模型占位文件应直接删除");
        File.Exists(Path.Combine(
                repositoryRoot,
                "src/AcceptanceSpecSystem.Core/Matching/Interfaces/IMatchingKnowledgeProvider.cs"
                    .Replace('/', Path.DirectorySeparatorChar)))
            .Should()
            .BeFalse("matching knowledge provider 占位文件应直接删除");
    }

    [Fact]
    public void Build_WhenEntitySurfaceDiffersOnlyByCompanySuffix_ShouldNotEmitLocalPositiveEntityEvidence()
    {
        var evidence = _builder.Build(
            new MatchSource
            {
                Project = "设备要求",
                Specification = "品牌：华为有限公司"
            },
            new MatchCandidate
            {
                SpecId = 2,
                Project = "设备要求",
                Specification = "品牌：华为"
            });

        evidence.Entities.Should().BeEmpty();
    }

    [Fact]
    public void Build_WhenIdentifierConflicts_ShouldKeepConflictAsEvidenceOnly()
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
            });

        evidence.Identifiers.Should().ContainSingle();
        evidence.Identifiers[0].Relation.Should().Be(EvidenceRelation.Conflict);
        evidence.Issues.Should().Contain(issue =>
            issue.Code == "identifier_conflict" &&
            issue.SourceValue == "ABC-100" &&
            issue.CandidateValue == "ABC-700");
    }

    [Fact]
    public void Build_WhenDifferentEntitySurfacesWithoutAliasRules_ShouldKeepEntityEvidenceEmpty()
    {
        var evidence = _builder.Build(
            new MatchSource
            {
                Project = "Panasonic 设备",
                Specification = "品牌：Panasonic"
            },
            new MatchCandidate
            {
                SpecId = 4,
                Project = "松下 设备",
                Specification = "品牌：松下"
            });

        evidence.Entities.Should().BeEmpty();
    }

    [Fact]
    public void Build_WhenMultipleIdentifiersExist_ShouldCaptureRemainingIdentifierConflictsWithoutHardConflict()
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
            });

        evidence.Identifiers.Should().HaveCount(2);
        evidence.Identifiers.Should().Contain(item =>
            item.SourceValue == "ABC-100" &&
            item.CandidateValue == "ABC-100" &&
            item.Relation == EvidenceRelation.Exact);
        evidence.Identifiers.Should().Contain(item =>
            item.SourceValue == "XYZ-200" &&
            item.CandidateValue == "XYZ-201" &&
            item.Relation == EvidenceRelation.Conflict);
    }

    [Fact]
    public void Build_WhenSameEnglishEntityDiffersOnlyByCase_ShouldKeepEntityEvidenceEmptyForAiGate()
    {
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
            });

        evidence.Entities.Should().BeEmpty();
    }

    [Fact]
    public void Build_WhenOnlyLocalNumericUnitReasoningCouldHelp_ShouldNotEmitNumericEvidenceOrIssues()
    {
        var evidence = _builder.Build(
            new MatchSource
            {
                Project = "芯片工艺",
                Specification = "线宽等于0.13μm"
            },
            new MatchCandidate
            {
                SpecId = 80,
                Project = "芯片工艺",
                Specification = "线宽等于130nm"
            });

        AssertAiOnlyNumericBehavior(evidence);
    }

    [Fact]
    public void Build_WhenNumericValueDiffers_ShouldNotEmitLocalNumericConflictIssue()
    {
        var evidence = _builder.Build(
            new MatchSource
            {
                Project = "电压要求",
                Specification = "电压等于24V"
            },
            new MatchCandidate
            {
                SpecId = 81,
                Project = "电压要求",
                Specification = "电压等于2.4V"
            });

        AssertAiOnlyNumericBehavior(evidence);
    }

    [Fact]
    public void Build_WhenDifferentSemanticSlotsContainMeasurements_ShouldStillIgnoreLocalNumericInference()
    {
        var evidence = _builder.Build(
            new MatchSource
            {
                Project = "安全光栅要求",
                Specification = "离地最低处为360mm，离地最高处为1200mm"
            },
            new MatchCandidate
            {
                SpecId = 82,
                Project = "安全光栅要求",
                Specification = "离地最低处为1200mm，离地最高处为360mm"
            });

        AssertAiOnlyNumericBehavior(evidence);
    }

    private static void AssertAiOnlyNumericBehavior(MatchEvidence evidence)
    {
        evidence.NumericConstraints.Should().BeEmpty("数值/单位/方向类判断改由 Embedding + AI 处理");
        evidence.Issues.Should().NotContain(issue =>
            string.Equals(issue.Code, "numeric_value_conflict", StringComparison.Ordinal) ||
            string.Equals(issue.Code, "evidence_insufficient", StringComparison.Ordinal) ||
            string.Equals(issue.Code, "numeric_fragment_mismatch", StringComparison.Ordinal));
        evidence.Warnings.Should().BeEmpty();
        evidence.Conflicts.Should().BeEmpty();
        evidence.Summary.Should().BeEmpty();
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
