using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class SmartStructureHeaderGapAnalyzerTests
{
    [Fact]
    public void Analyze_ShouldSeparateGlobalAndEffectiveRuleGaps()
    {
        var templates = new[]
        {
            new DocumentTemplate
            {
                Id = 10,
                CustomerId = 1,
                TemplateName = "客户1模板",
                HeadersJson = """["控制限","检验结论","备注"]""",
                SpecificationColumnIndex = 0,
                AcceptanceColumnIndex = 1,
                RemarkColumnIndex = 2,
                ConfirmedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new DocumentTemplate
            {
                Id = 20,
                CustomerId = 2,
                TemplateName = "客户2模板",
                HeadersJson = """["控制限","验收"]""",
                SpecificationColumnIndex = 0,
                AcceptanceColumnIndex = 1,
                ConfirmedAt = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        var rules = new[]
        {
            new ColumnMappingRule
            {
                CustomerId = null,
                TargetField = ColumnMappingTargetField.Specification,
                MatchMode = ColumnMappingMatchMode.Contains,
                Pattern = "规格",
                Enabled = true
            },
            new ColumnMappingRule
            {
                CustomerId = null,
                TargetField = ColumnMappingTargetField.Acceptance,
                MatchMode = ColumnMappingMatchMode.Regex,
                Pattern = "检验.*结论",
                Enabled = true
            },
            new ColumnMappingRule
            {
                CustomerId = 1,
                TargetField = ColumnMappingTargetField.Specification,
                MatchMode = ColumnMappingMatchMode.Equals,
                Pattern = "控制限",
                Enabled = true
            }
        };

        var report = SmartStructureHeaderGapAnalyzer.Analyze(templates, rules, topN: 10);

        var globalGap = report.GlobalUncoveredHeaders.Should().ContainSingle(item =>
            item.Header == "控制限" &&
            item.TargetField == ColumnMappingTargetField.Specification).Subject;
        globalGap.OccurrenceCount.Should().Be(2);
        globalGap.CustomerCount.Should().Be(2);
        globalGap.CustomerIds.Should().BeEquivalentTo([1, 2]);

        var effectiveGap = report.EffectiveUncoveredHeaders.Should().ContainSingle(item =>
            item.Header == "控制限" &&
            item.TargetField == ColumnMappingTargetField.Specification).Subject;
        effectiveGap.OccurrenceCount.Should().Be(1);
        effectiveGap.CustomerIds.Should().BeEquivalentTo([2]);

        report.GlobalUncoveredHeaders.Should().NotContain(item => item.Header == "检验结论");
        report.EffectiveUncoveredHeaders.Should().NotContain(item => item.Header == "检验结论");
    }

    [Fact]
    public void Analyze_ShouldCollectLearnedRulesNotCoveredByGlobalRules()
    {
        var rules = new[]
        {
            new ColumnMappingRule
            {
                CustomerId = 1,
                Source = ColumnMappingRuleSource.Learned,
                TargetField = ColumnMappingTargetField.Specification,
                MatchMode = ColumnMappingMatchMode.Equals,
                Pattern = "允收范围",
                Enabled = true
            },
            new ColumnMappingRule
            {
                CustomerId = 2,
                Source = ColumnMappingRuleSource.Learned,
                TargetField = ColumnMappingTargetField.Specification,
                MatchMode = ColumnMappingMatchMode.Equals,
                Pattern = "允收范围",
                Enabled = true
            },
            new ColumnMappingRule
            {
                CustomerId = 3,
                Source = ColumnMappingRuleSource.Learned,
                TargetField = ColumnMappingTargetField.Acceptance,
                MatchMode = ColumnMappingMatchMode.Equals,
                Pattern = "检验结论",
                Enabled = true
            },
            new ColumnMappingRule
            {
                CustomerId = null,
                Source = ColumnMappingRuleSource.Builtin,
                TargetField = ColumnMappingTargetField.Acceptance,
                MatchMode = ColumnMappingMatchMode.Regex,
                Pattern = "检验.*结论",
                Enabled = true
            }
        };

        var report = SmartStructureHeaderGapAnalyzer.Analyze([], rules, topN: 10);

        var candidate = report.LearnedRuleGlobalCandidates.Should().ContainSingle().Subject;
        candidate.Header.Should().Be("允收范围");
        candidate.TargetField.Should().Be(ColumnMappingTargetField.Specification);
        candidate.OccurrenceCount.Should().Be(2);
        candidate.CustomerCount.Should().Be(2);
        candidate.CustomerIds.Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public void Analyze_WithContainsRule_ShouldUseRuntimeFuzzyFallback()
    {
        var templates = new[]
        {
            new DocumentTemplate
            {
                Id = 10,
                CustomerId = 1,
                TemplateName = "近似表头模板",
                HeadersJson = """["设备规格要球"]""",
                SpecificationColumnIndex = 0,
                ConfirmedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        var rules = new[]
        {
            new ColumnMappingRule
            {
                CustomerId = null,
                TargetField = ColumnMappingTargetField.Specification,
                MatchMode = ColumnMappingMatchMode.Contains,
                Pattern = "设备规格要求",
                Enabled = true
            }
        };

        var report = SmartStructureHeaderGapAnalyzer.Analyze(templates, rules, topN: 10);

        report.GlobalUncoveredHeaders.Should().BeEmpty();
        report.EffectiveUncoveredHeaders.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_ShouldSummarizeStageOneConclusion()
    {
        var templates = new[]
        {
            new DocumentTemplate
            {
                Id = 10,
                CustomerId = 1,
                TemplateName = "客户1模板",
                HeadersJson = """["项目","允收范围","检验结论"]""",
                ProjectColumnIndex = 0,
                SpecificationColumnIndex = 1,
                AcceptanceColumnIndex = 2,
                ConfirmedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new DocumentTemplate
            {
                Id = 20,
                CustomerId = 2,
                TemplateName = "客户2模板",
                HeadersJson = """["项目","允收范围","检验结论"]""",
                ProjectColumnIndex = 0,
                SpecificationColumnIndex = 1,
                AcceptanceColumnIndex = 2,
                ConfirmedAt = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        var report = SmartStructureHeaderGapAnalyzer.Analyze(
            templates,
            [
                new ColumnMappingRule
                {
                    CustomerId = null,
                    TargetField = ColumnMappingTargetField.Project,
                    MatchMode = ColumnMappingMatchMode.Contains,
                    Pattern = "项目",
                    Enabled = true
                },
                new ColumnMappingRule
                {
                    CustomerId = null,
                    TargetField = ColumnMappingTargetField.Acceptance,
                    MatchMode = ColumnMappingMatchMode.Contains,
                    Pattern = "检验",
                    Enabled = true
                }
            ],
            topN: 10);

        report.Conclusion.HasMappingSignals.Should().BeTrue();
        report.Conclusion.RuleBackfillCandidateCount.Should().Be(1);
        report.Conclusion.LearnedRulePromotionCandidateCount.Should().Be(0);
        report.Conclusion.EffectiveRuntimeGapCount.Should().Be(1);
        report.Conclusion.NextAction.Should().Be(SmartStructureHeaderGapNextAction.ReviewRuleBackfillFirst);
    }

    [Fact]
    public void Analyze_ShouldNotLimitConclusionByTopN()
    {
        var templates = Enumerable.Range(1, 3)
            .Select(index => new DocumentTemplate
            {
                Id = index,
                CustomerId = index,
                TemplateName = $"客户{index}模板",
                HeadersJson = $"""["项目","规格别名{index}"]""",
                ProjectColumnIndex = 0,
                SpecificationColumnIndex = 1,
                ConfirmedAt = new DateTime(2026, 7, index, 0, 0, 0, DateTimeKind.Utc)
            })
            .ToList();

        var report = SmartStructureHeaderGapAnalyzer.Analyze(
            templates,
            [
                new ColumnMappingRule
                {
                    CustomerId = null,
                    TargetField = ColumnMappingTargetField.Project,
                    MatchMode = ColumnMappingMatchMode.Contains,
                    Pattern = "项目",
                    Enabled = true
                }
            ],
            topN: 1);

        report.GlobalUncoveredHeaders.Should().HaveCount(1);
        report.Conclusion.EffectiveRuntimeGapCount.Should().Be(3);
    }

    [Fact]
    public void SampleLoader_ShouldConvertJsonSamplesToTemplateObservations()
    {
        const string samplesJson = """
        [
          {
            "customerId": 7,
            "templateName": "离线样本",
            "headers": ["项目", "允收范围", "检验结论"],
            "projectColumnIndex": 0,
            "specificationColumnIndex": 1,
            "acceptanceColumnIndex": 2
          }
        ]
        """;

        var templates = SmartStructureHeaderGapSampleLoader.LoadFromJson(samplesJson);

        var report = SmartStructureHeaderGapAnalyzer.Analyze(
            templates,
            [
                new ColumnMappingRule
                {
                    CustomerId = null,
                    TargetField = ColumnMappingTargetField.Project,
                    MatchMode = ColumnMappingMatchMode.Contains,
                    Pattern = "项目",
                    Enabled = true
                }
            ],
            topN: 10);

        report.TemplateCount.Should().Be(1);
        report.GlobalUncoveredHeaders.Should().Contain(item =>
            item.Header == "允收范围" &&
            item.TargetField == ColumnMappingTargetField.Specification);
        report.GlobalUncoveredHeaders.Should().Contain(item =>
            item.Header == "检验结论" &&
            item.TargetField == ColumnMappingTargetField.Acceptance);
    }
}
