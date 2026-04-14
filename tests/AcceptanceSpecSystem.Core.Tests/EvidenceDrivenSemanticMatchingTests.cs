using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Core.Tests;

public class EvidenceDrivenSemanticMatchingTests
{
    [Fact]
    public async Task BatchMatch_WhenFinalScoreBelowHighConfidenceThreshold_ShouldRequireManualReview()
    {
        var source = new MatchSource
        {
            Project = "安装要求",
            Specification = "设备位置需要预留维护空间"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 501,
                Project = "安装要求",
                Specification = "设备位置需预留空间"
            }
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f], defaultCandidateEmbedding: [0.7f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.95
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(501);
        result.Results[0].Score.Should().BeLessThan(0.95);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
    }

    [Fact]
    public async Task BatchMatch_WhenCustomHighConfidenceThresholdIsLower_ShouldAutoApply()
    {
        var source = new MatchSource
        {
            Project = "安装要求",
            Specification = "设备位置"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 502,
                Project = "安装要求",
                Specification = "设备位置"
            }
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f], defaultCandidateEmbedding: [0.4f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.65
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(502);
        result.Results[0].Score.Should().BeGreaterThan(0.65);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
    }

    [Fact]
    public async Task BatchMatch_WhenSourceEmbeddingBatchReturnsTooFewVectors_ShouldFailFast()
    {
        var sources = new List<MatchSource>
        {
            new() { Project = "项目A", Specification = "规格A" },
            new() { Project = "项目B", Specification = "规格B" }
        };
        var candidates = new List<MatchCandidate>
        {
            new() { SpecId = 1, Project = "项目A", Specification = "规格A" }
        };

        var service = new SemanticKernelMatchingService(
            new ShortEmbeddingBatchService(sourceBatchCount: 1),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var act = () => service.BatchMatchAsync(
            sources,
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1
            });

        await act.Should().ThrowAsync<AiServiceUnavailableException>();
    }

    [Fact]
    public async Task BatchMatch_WhenCandidateEmbeddingBatchReturnsTooFewVectors_ShouldFailFast()
    {
        var source = new MatchSource
        {
            Project = "项目A",
            Specification = "规格A"
        };
        var candidates = new List<MatchCandidate>
        {
            new() { SpecId = 1, Project = "项目A", Specification = "规格A" },
            new() { SpecId = 2, Project = "项目A", Specification = "规格B" }
        };

        var service = new SemanticKernelMatchingService(
            new ShortEmbeddingBatchService(candidateBatchCount: 1),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var act = () => service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 2
            });

        await act.Should().ThrowAsync<AiServiceUnavailableException>();
    }

    [Fact]
    public async Task BatchMatch_WhenConflictAndCompatibleCandidatesCompete_ShouldPreferCompatibleCandidate()
    {
        var source = new MatchSource
        {
            Project = "尺寸要求",
            Specification = "宽度小于0.5cm"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 1,
                Project = "尺寸要求",
                Specification = "宽度等于0.7cm",
                Acceptance = "RISKY",
                Embedding = [0.99f]
            },
            new()
            {
                SpecId = 2,
                Project = "尺寸要求",
                Specification = "宽度等于0.2cm",
                Acceptance = "SAFE",
                Embedding = [0.90f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 2,
                AmbiguityMargin = 0.01
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(2);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].Evidence.NumericConstraints.Should().ContainSingle();
        result.Results[0].Evidence.NumericConstraints[0].Relation.Should().Be(EvidenceRelation.Compatible);
    }

    [Fact]
    public async Task BatchMatch_WhenOnlyHardConflictCandidateExists_ShouldRejectAutoApply()
    {
        var source = new MatchSource
        {
            Project = "尺寸要求",
            Specification = "宽度小于0.5cm"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 10,
                Project = "尺寸要求",
                Specification = "宽度等于0.7cm",
                Acceptance = "RISKY",
                Embedding = [0.99f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                AmbiguityMargin = 0.01
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(10);
        result.Results[0].Decision.Should().Be(MatchDecision.Reject);
        result.Results[0].IsHighConfidence.Should().BeFalse();
        result.Results[0].Evidence.HasHardConflict.Should().BeTrue();
    }

    [Fact]
    public async Task BatchMatch_WhenCustomKnowledgeDefinesConflictPair_ShouldUseProviderKnowledge()
    {
        var source = new MatchSource
        {
            Project = "正转模式",
            Specification = "速度 100 mm/s"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 88,
                Project = "反转模式",
                Specification = "速度 100 mm/s",
                Acceptance = "RISKY",
                Embedding = [0.99f]
            }
        };

        var knowledge = new MatchingKnowledge
        {
            ConflictPairs = [("正转", "反转")]
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            knowledgeProvider: new FixedMatchingKnowledgeProvider(knowledge));

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                AmbiguityMargin = 0.01
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(88);
        result.Results[0].Decision.Should().Be(MatchDecision.Reject);
    }

    [Fact]
    public async Task BatchMatch_WhenDefaultKnowledgeDetectsShoubanjiVsFangbanji_ShouldRejectCandidate()
    {
        var source = new MatchSource
        {
            Project = "设备设计要求",
            Specification = "收板机生产载位对接AGV,安全光栅有效范围离地最低处为360mm"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 889,
                Project = "设备设计要求",
                Specification = "放板机生产载位对接AGV,安全光栅有效范围离地最低处为360mm",
                Acceptance = "RISKY",
                Embedding = [0.99f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                AmbiguityMargin = 0.01
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(889);
        result.Results[0].Decision.Should().Be(MatchDecision.Reject);
        result.Results[0].Evidence.HasHardConflict.Should().BeTrue();
        result.Results[0].Evidence.Conflicts.Should().Contain(item =>
            item.Contains("收板", StringComparison.Ordinal) &&
            item.Contains("放板", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BatchMatch_WhenLlmEntityResolutionMatchesAliasWithoutKnowledge_ShouldAddEntityEvidence()
    {
        var source = new MatchSource
        {
            Project = "设备要求",
            Specification = "Panasonic 设备需安装防护罩"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 188,
                Project = "设备要求",
                Specification = "松下设备需安装防护罩",
                Acceptance = "OK",
                Embedding = [0.98f]
            }
        };

        var entityResolutionService = new FixedLlmEntityResolutionService(new LlmEntityResolutionResult
        {
            Relation = LlmEntityRelation.AliasSame,
            Confidence = 0.95,
            NormalizedEntity = "松下",
            Reason = "Panasonic 与 松下是同一品牌的中英文名称"
        });

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            knowledgeProvider: new FixedMatchingKnowledgeProvider(CreateKnowledgeWithoutEntityAliases()),
            llmEntityResolutionService: entityResolutionService);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                UseLlmEntityResolution = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(188);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].Evidence.Entities.Should().ContainSingle(entity =>
            entity.Relation == EvidenceRelation.AliasSame &&
            entity.SourceValue == "Panasonic" &&
            entity.CandidateValue == "松下");
        entityResolutionService.Requests.Should().ContainSingle();
        entityResolutionService.Requests[0].SourceEntity.Should().Be("Panasonic");
        entityResolutionService.Requests[0].CandidateEntity.Should().Be("松下");
    }

    [Fact]
    public async Task BatchMatch_WhenLlmEntityResolutionFindsHighConfidenceConflict_ShouldRejectCandidate()
    {
        var source = new MatchSource
        {
            Project = "设备要求",
            Specification = "Panasonic 设备需安装防护罩"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 189,
                Project = "设备要求",
                Specification = "Mitsubishi 设备需安装防护罩",
                Acceptance = "NG",
                Embedding = [0.98f]
            }
        };

        var entityResolutionService = new FixedLlmEntityResolutionService(new LlmEntityResolutionResult
        {
            Relation = LlmEntityRelation.Conflict,
            Confidence = 0.95,
            Reason = "Panasonic 与 Mitsubishi 是不同品牌"
        });

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            knowledgeProvider: new FixedMatchingKnowledgeProvider(CreateKnowledgeWithoutEntityAliases()),
            llmEntityResolutionService: entityResolutionService);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                UseLlmEntityResolution = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(189);
        result.Results[0].Decision.Should().Be(MatchDecision.Reject);
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "entity_conflict" &&
            issue.SourceValue == "Panasonic" &&
            issue.CandidateValue == "Mitsubishi");
    }

    [Fact]
    public async Task BatchMatch_WhenLlmEntityResolutionReturnsUnknown_ShouldRequireManualReview()
    {
        var source = new MatchSource
        {
            Project = "设备要求",
            Specification = "XJTech 设备需安装防护罩"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 190,
                Project = "设备要求",
                Specification = "新境科技设备需安装防护罩",
                Acceptance = "CHECK",
                Embedding = [0.98f]
            }
        };

        var entityResolutionService = new FixedLlmEntityResolutionService(new LlmEntityResolutionResult
        {
            Relation = LlmEntityRelation.Unknown,
            Confidence = 0.55,
            Reason = "缺少足够证据确认两者是否同一品牌"
        });

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            knowledgeProvider: new FixedMatchingKnowledgeProvider(CreateKnowledgeWithoutEntityAliases()),
            llmEntityResolutionService: entityResolutionService);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                UseLlmEntityResolution = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(190);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Evidence.HasHardConflict.Should().BeFalse();
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "entity_unknown" &&
            issue.SourceValue == "XJTech" &&
            issue.CandidateValue == "新境科技");
    }

    [Fact]
    public async Task BatchMatch_WhenLlmEquivalenceReturnsEquivalent_ShouldAutoApplyAndTreatAsHighConfidence()
    {
        var source = new MatchSource
        {
            Project = "安装要求",
            Specification = "最大不可拆部件≈3200"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 192,
                Project = "安装要求",
                Specification = "最大不可拆部件约等于3200。",
                Embedding = [0.88f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 0.92,
            Reason = "≈ 与 约等于属于同义表达"
        });

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: equivalenceService);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98,
                UseLlmReview = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(192);
        result.Results[0].Score.Should().BeLessThan(0.98);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].IsHighConfidence.Should().BeTrue();
        var equivalentResult = result.Results[0].LlmEquivalence;
        equivalentResult.Should().NotBeNull();
        equivalentResult!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        equivalentResult.ReasonType.Should().Be(LlmEquivalenceReasonType.EquivalentExpression);
        equivalenceService.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task BatchMatch_WhenEquivalentButStillAmbiguous_ShouldKeepManualReview()
    {
        var source = new MatchSource
        {
            Project = "安装要求",
            Specification = "最大不可拆部件≈3200"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 195,
                Project = "安装要求",
                Specification = "最大不可拆部件约等于3200。",
                Embedding = [0.88f]
            },
            new()
            {
                SpecId = 194,
                Project = "安装要求",
                Specification = "最大不可拆部件近似3200。",
                Embedding = [0.88f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 0.9,
            Reason = "≈ 与约等于属于等价表达"
        });

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: equivalenceService);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 2,
                AmbiguityMargin = 0.02,
                HighConfidenceThreshold = 0.98,
                UseLlmReview = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(195);
        result.Results[0].IsAmbiguous.Should().BeTrue();
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].IsHighConfidence.Should().BeFalse();
        result.Results[0].LlmEquivalence.Should().NotBeNull();
        result.Results[0].LlmEquivalence!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        equivalenceService.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task BatchMatch_WhenLlmEquivalenceReturnsUncertain_ShouldRequireManualReview()
    {
        var source = new MatchSource
        {
            Project = "安装要求",
            Specification = "最大不可拆部件≈3200"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 193,
                Project = "安装要求",
                Specification = "最大不可拆部件约为3200",
                Embedding = [0.88f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Uncertain,
            ReasonType = LlmEquivalenceReasonType.Uncertain,
            Confidence = 0.45,
            Reason = "上下文不足，无法确认是否完全等价"
        });

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: equivalenceService);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98,
                UseLlmReview = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(193);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].IsHighConfidence.Should().BeFalse();
        result.Results[0].LlmEquivalence.Should().NotBeNull();
        result.Results[0].LlmEquivalence!.Verdict.Should().Be(LlmEquivalenceVerdict.Uncertain);
    }

    [Fact]
    public async Task BatchMatch_WhenLlmEquivalenceReturnsDifferent_ShouldRequireManualReview()
    {
        var source = new MatchSource
        {
            Project = "安装要求",
            Specification = "最大不可拆部件≈3200"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 194,
                Project = "安装要求",
                Specification = "最大不可拆部件约为2200",
                Embedding = [0.88f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Different,
            ReasonType = LlmEquivalenceReasonType.SemanticDifference,
            Confidence = 0.91,
            Reason = "关键数值不同，语义不等价"
        });

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: equivalenceService);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98,
                UseLlmReview = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(194);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        var differentResult = result.Results[0].LlmEquivalence;
        differentResult.Should().NotBeNull();
        differentResult!.Verdict.Should().Be(LlmEquivalenceVerdict.Different);
        differentResult.ReasonType.Should().Be(LlmEquivalenceReasonType.SemanticDifference);
    }

    [Fact]
    public async Task BatchMatch_WhenLlmEquivalenceReturnsNull_ShouldFallbackToUncertainManualReview()
    {
        var source = new MatchSource
        {
            Project = "安装要求",
            Specification = "最大不可拆部件≈3200"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 195,
                Project = "安装要求",
                Specification = "最大不可拆部件约等于3200。",
                Embedding = [0.88f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(null);
        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: equivalenceService);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98,
                UseLlmReview = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        var nullFallbackResult = result.Results[0].LlmEquivalence;
        nullFallbackResult.Should().NotBeNull();
        nullFallbackResult!.Verdict.Should().Be(LlmEquivalenceVerdict.Uncertain);
        nullFallbackResult.Reason.Should().Contain("未返回有效结果");
    }

    [Fact]
    public async Task BatchMatch_WhenLlmEquivalenceThrows_ShouldFallbackToUncertainManualReview()
    {
        var source = new MatchSource
        {
            Project = "安装要求",
            Specification = "最大不可拆部件≈3200"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 196,
                Project = "安装要求",
                Specification = "最大不可拆部件约等于3200。",
                Embedding = [0.88f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: new ThrowingLlmEquivalenceAdjudicationService());

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98,
                UseLlmReview = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        var exceptionFallbackResult = result.Results[0].LlmEquivalence;
        exceptionFallbackResult.Should().NotBeNull();
        exceptionFallbackResult!.Verdict.Should().Be(LlmEquivalenceVerdict.Uncertain);
        exceptionFallbackResult.Reason.Should().Contain("裁决失败");
    }

    [Fact]
    public async Task BatchMatch_WhenNumericConflictExists_ShouldNotBeOverriddenByEntityAlias()
    {
        var source = new MatchSource
        {
            Project = "电压要求",
            Specification = "Panasonic 设备电压等于24V"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 191,
                Project = "电压要求",
                Specification = "松下设备电压等于2.4V",
                Embedding = [0.99f]
            }
        };

        var entityResolutionService = new FixedLlmEntityResolutionService(new LlmEntityResolutionResult
        {
            Relation = LlmEntityRelation.AliasSame,
            Confidence = 0.95,
            NormalizedEntity = "松下",
            Reason = "Panasonic 与 松下是同一品牌"
        });

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            knowledgeProvider: new FixedMatchingKnowledgeProvider(CreateKnowledgeWithoutEntityAliases()),
            llmEntityResolutionService: entityResolutionService);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                UseLlmEntityResolution = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.Reject);
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "numeric_value_conflict" &&
            issue.FieldName == "电压");
    }

    [Fact]
    public async Task BatchMatch_WhenScoreBelowHighConfidenceThreshold_ShouldRequireManualReview()
    {
        var source = new MatchSource
        {
            Project = "设备安装需求",
            Specification = "设备供应商在到厂前提供设备的空压位置大小及流量"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 501,
                Project = "设备安装需求",
                Specification = "设备供应商在到厂前提供设备的空压位置及流量要求",
                Acceptance = "NEAR",
                Embedding = [0.96f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                AmbiguityMargin = 0.01,
                HighConfidenceThreshold = 0.95
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(501);
        result.Results[0].Score.Should().BeGreaterThan(MatchingThresholds.MediumConfidenceScore);
        result.Results[0].Score.Should().BeLessThan(0.95);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].IsHighConfidence.Should().BeFalse();
    }

    [Fact]
    public async Task BatchMatch_WhenEmbeddingResponseCountMismatchesSourceCount_ShouldFailFast()
    {
        var service = new SemanticKernelMatchingService(
            new ShortBatchEmbeddingService(
                sourceEmbeddings: [[1f]],
                candidateEmbeddings: [[1f]]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var act = () => service.BatchMatchAsync(
            [
                new MatchSource { Project = "项目A", Specification = "规格A" },
                new MatchSource { Project = "项目B", Specification = "规格B" }
            ],
            [
                new MatchCandidate { SpecId = 1, Project = "项目A", Specification = "规格A" }
            ],
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0
            });

        await act.Should()
            .ThrowAsync<AiServiceUnavailableException>()
            .WithMessage("*返回数量与请求不一致*");
    }

    [Fact]
    public async Task BatchMatch_ShouldNormalizeComparableTextAlongRealMatchingPath()
    {
        var source = new MatchSource
        {
            Project = "安装要求",
            Specification = "安全光栅（离地  360mm）"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 601,
                Project = "安装要求",
                Specification = "安全光栅(离地 360mm)",
                Acceptance = "OK",
                Embedding = [0.90f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                AmbiguityMargin = 0.01
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(601);
        result.Results[0].ScoreDetails["SpecificationText"].Should().Be(1.0);
    }

    [Fact]
    public async Task BatchMatch_WhenLaterNumericConstraintConflicts_ShouldRejectCandidate()
    {
        var source = new MatchSource
        {
            Project = "尺寸要求",
            Specification = "宽度等于10mm，高度等于20mm"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 601,
                Project = "尺寸要求",
                Specification = "宽度等于10mm，高度等于30mm",
                Embedding = [0.99f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.Reject);
        result.Results[0].Evidence.NumericConstraints.Should().HaveCount(2);
        result.Results[0].Evidence.Conflicts.Should().Contain(item => item.Contains("高度", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BatchMatch_WhenVoltageAlternativeContainsDigitLoss_ShouldRejectCandidate()
    {
        var source = new MatchSource
        {
            Project = "水/电/气",
            Specification = "电力规格要求: 380V三相/50HZ或22V/50HZ；气压需求≤6kg/cm3"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 602,
                Project = "水/电/气",
                Specification = "电力规格要求: 380V三相/50HZ或220V/50HZ；气压需求≤6kg/cm3",
                Embedding = [0.999f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.95
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.Reject);
        result.Results[0].Evidence.NumericConstraints.Should().Contain(item =>
            item.FieldName == "电压" &&
            item.Relation == EvidenceRelation.Conflict);
        result.Results[0].Evidence.Conflicts.Should().Contain(item =>
            item.Contains("22V", StringComparison.OrdinalIgnoreCase) &&
            item.Contains("220V", StringComparison.OrdinalIgnoreCase));
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "numeric_value_conflict" &&
            issue.FieldName == "电压" &&
            issue.SourceValue == "22V" &&
            issue.CandidateValue == "220V" &&
            issue.Message.Contains("22V", StringComparison.OrdinalIgnoreCase) &&
            issue.Message.Contains("220V", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BatchMatch_WhenVoltageDecimalPointMismatch_ShouldExposeStructuredIssue()
    {
        var source = new MatchSource
        {
            Project = "电压要求",
            Specification = "电压等于24V"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 603,
                Project = "电压要求",
                Specification = "电压等于2.4V",
                Embedding = [0.999f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.Reject);
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "numeric_value_conflict" &&
            issue.FieldName == "电压" &&
            issue.SourceValue == "24V" &&
            issue.CandidateValue == "2.4V" &&
            issue.Message.Contains("小数点", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BatchMatch_WhenCurrentDecimalPointMismatch_ShouldExposeStructuredIssue()
    {
        var source = new MatchSource
        {
            Project = "电流要求",
            Specification = "电流等于2A"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 604,
                Project = "电流要求",
                Specification = "电流等于0.2A",
                Embedding = [0.999f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.Reject);
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "numeric_value_conflict" &&
            issue.FieldName == "电流" &&
            issue.SourceValue == "2A" &&
            issue.CandidateValue == "0.2A" &&
            issue.Message.Contains("数量级", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BatchMatch_WhenLaterIdentifierConflicts_ShouldRejectCandidate()
    {
        var source = new MatchSource
        {
            Project = "设备 ABC-100",
            Specification = "备用型号 XYZ-200"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 701,
                Project = "设备 ABC-100",
                Specification = "备用型号 XYZ-300",
                Embedding = [0.99f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.Reject);
        result.Results[0].Evidence.Identifiers.Should().HaveCount(2);
        result.Results[0].Evidence.Conflicts.Should().Contain(item => item.Contains("XYZ-200", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BatchMatch_WhenComparableTextOnlyDiffersByWhitespaceAndFullWidthBrackets_ShouldStillTreatAsExact()
    {
        var source = new MatchSource
        {
            Project = "  设备（主线）  ",
            Specification = "  安装   位置  "
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 801,
                Project = "设备(主线)",
                Specification = "安装 位置",
                Embedding = [1f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f], defaultCandidateEmbedding: [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.95
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(801);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].ScoreDetails["ProjectMatch"].Should().Be(1.0);
        result.Results[0].ScoreDetails["SpecificationText"].Should().Be(1.0);
    }

    [Fact]
    public async Task BatchMatch_WhenSpecificationIsExactlySameButNoKeywordToken_ShouldTreatKeywordScoreAsExact()
    {
        var source = new MatchSource
        {
            Project = "设备交货时间",
            Specification = "<80天;"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 802,
                Project = "设备交货时间",
                Specification = "<80天;",
                Embedding = [1f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f], defaultCandidateEmbedding: [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(802);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].Score.Should().Be(1.0);
        result.Results[0].ScoreDetails["KeywordOverlap"].Should().Be(1.0);
    }

    private sealed class FixedSourceEmbeddingService : IEmbeddingService
    {
        private readonly string _sourceText;
        private readonly float[] _sourceEmbedding;
        private readonly float[] _defaultCandidateEmbedding;

        public FixedSourceEmbeddingService(string sourceText, float[] sourceEmbedding, float[]? defaultCandidateEmbedding = null)
        {
            _sourceText = sourceText;
            _sourceEmbedding = sourceEmbedding;
            _defaultCandidateEmbedding = defaultCandidateEmbedding ?? [0f];
        }

        public bool IsAvailable => true;

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(text == _sourceText ? _sourceEmbedding : _defaultCandidateEmbedding);
        }

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            var list = texts.Select(text => text == _sourceText ? _sourceEmbedding : _defaultCandidateEmbedding).ToList();
            return Task.FromResult(list);
        }

        public double ComputeSimilarity(float[] embedding1, float[] embedding2)
        {
            if (embedding1.Length == 0 || embedding2.Length == 0)
                return 0;

            return embedding1.Zip(embedding2, (left, right) => left * right).Sum();
        }
    }

    private sealed class ShortEmbeddingBatchService : IEmbeddingService
    {
        private readonly int _sourceBatchCount;
        private readonly int _candidateBatchCount;
        private int _batchCallIndex;

        public ShortEmbeddingBatchService(int sourceBatchCount = int.MaxValue, int candidateBatchCount = int.MaxValue)
        {
            _sourceBatchCount = sourceBatchCount;
            _candidateBatchCount = candidateBatchCount;
        }

        public bool IsAvailable => true;

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new[] { 1f });
        }

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            var input = texts.ToList();
            var callIndex = Interlocked.Increment(ref _batchCallIndex);
            var isSourceBatch = callIndex == 1;
            var count = isSourceBatch ? _sourceBatchCount : _candidateBatchCount;
            var actualCount = Math.Min(input.Count, count);
            var result = Enumerable.Range(0, actualCount)
                .Select(_ => new[] { 1f })
                .ToList();
            return Task.FromResult(result);
        }

        public double ComputeSimilarity(float[] embedding1, float[] embedding2)
        {
            if (embedding1.Length == 0 || embedding2.Length == 0)
                return 0;

            return embedding1.Zip(embedding2, (left, right) => left * right).Sum();
        }
    }

    private sealed class FixedMatchingKnowledgeProvider : IMatchingKnowledgeProvider
    {
        private readonly MatchingKnowledge _knowledge;

        public FixedMatchingKnowledgeProvider(MatchingKnowledge knowledge)
        {
            _knowledge = knowledge;
        }

        public Task<MatchingKnowledge> GetKnowledgeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_knowledge);
        }
    }

    private static MatchingKnowledge CreateKnowledgeWithoutEntityAliases()
    {
        var knowledge = MatchingKnowledge.CreateDefault();
        knowledge.EntityAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return knowledge;
    }

    private sealed class FixedLlmEntityResolutionService : ILlmEntityResolutionService
    {
        private readonly LlmEntityResolutionResult? _result;

        public FixedLlmEntityResolutionService(LlmEntityResolutionResult? result)
        {
            _result = result;
        }

        public List<LlmEntityResolutionRequest> Requests { get; } = [];

        public Task<LlmEntityResolutionResult?> ResolveAsync(
            LlmEntityResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_result);
        }

        public bool TryParseEntityResolutionResult(string raw, out LlmEntityResolutionResult result)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FixedLlmEquivalenceAdjudicationService : ILlmEquivalenceAdjudicationService
    {
        private readonly LlmEquivalenceAdjudicationResult? _result;

        public FixedLlmEquivalenceAdjudicationService(LlmEquivalenceAdjudicationResult? result)
        {
            _result = result;
        }

        public List<LlmEquivalenceAdjudicationRequest> Requests { get; } = [];

        public Task<LlmEquivalenceAdjudicationResult?> AdjudicateAsync(
            LlmEquivalenceAdjudicationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_result);
        }

        public bool TryParseAdjudicationResult(string raw, out LlmEquivalenceAdjudicationResult result)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ThrowingLlmEquivalenceAdjudicationService : ILlmEquivalenceAdjudicationService
    {
        public Task<LlmEquivalenceAdjudicationResult?> AdjudicateAsync(
            LlmEquivalenceAdjudicationRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("模拟 AI 等价裁决异常");
        }

        public bool TryParseAdjudicationResult(string raw, out LlmEquivalenceAdjudicationResult result)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ShortBatchEmbeddingService : IEmbeddingService
    {
        private readonly List<float[]> _sourceEmbeddings;
        private readonly List<float[]> _candidateEmbeddings;
        private int _batchCallCount;

        public ShortBatchEmbeddingService(List<float[]> sourceEmbeddings, List<float[]> candidateEmbeddings)
        {
            _sourceEmbeddings = sourceEmbeddings;
            _candidateEmbeddings = candidateEmbeddings;
        }

        public bool IsAvailable => true;

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new[] { 1f });
        }

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            _batchCallCount++;
            var payload = _batchCallCount == 1 ? _sourceEmbeddings : _candidateEmbeddings;
            return Task.FromResult(payload);
        }

        public double ComputeSimilarity(float[] embedding1, float[] embedding2)
        {
            if (embedding1.Length == 0 || embedding2.Length == 0)
                return 0;

            return embedding1.Zip(embedding2, (left, right) => left * right).Sum();
        }
    }
}
