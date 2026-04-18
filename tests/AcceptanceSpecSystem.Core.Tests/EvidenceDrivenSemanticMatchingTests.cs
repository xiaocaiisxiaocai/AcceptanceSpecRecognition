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
    public async Task BatchMatch_WhenHighScoreWithoutAiEquivalence_ShouldRequireManualReview()
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
                Specification = "设备安装位置"
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.65
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(502);
        result.Results[0].Score.Should().BeGreaterThan(0.65);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
    }

    [Fact]
    public async Task BatchMatch_WhenSourceEmbeddingBatchReturnsTooFewVectors_ShouldFailFast()
    {
        var sources = new List<MatchSource>
        {
            new() { Project = "项目A", Specification = "规格A-源" },
            new() { Project = "项目B", Specification = "规格B-源" }
        };
        var candidates = new List<MatchCandidate>
        {
            new() { SpecId = 1, Project = "项目A", Specification = "规格A-候选" }
        };

        var service = new SemanticKernelMatchingService(
            new ShortEmbeddingBatchService(sourceBatchCount: 1),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var act = () => service.BatchMatchAsync(
            sources,
            candidates,
            new MatchingConfig
            {
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
            Specification = "规格A-源"
        };
        var candidates = new List<MatchCandidate>
        {
            new() { SpecId = 1, Project = "项目A", Specification = "规格A-候选" },
            new() { SpecId = 2, Project = "项目A", Specification = "规格B-候选" }
        };

        var service = new SemanticKernelMatchingService(
            new ShortEmbeddingBatchService(candidateBatchCount: 1),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var act = () => service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MinScoreThreshold = 0.0,
                RecallTopK = 2
            });

        await act.Should().ThrowAsync<AiServiceUnavailableException>();
    }

    [Fact]
    public async Task BatchMatch_WhenConflictAndCompatibleCandidatesCompete_ShouldFallBackToEmbeddingTopCandidateWithoutLocalNumericRerank()
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
                MinScoreThreshold = 0.0,
                RecallTopK = 2,
                AmbiguityMargin = 0.01
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(1);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Evidence.NumericConstraints.Should().BeEmpty();
        result.Results[0].Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task BatchMatch_WhenAiRerankSelectsLowerEmbeddingCandidate_ShouldPromoteItAndRunEquivalenceOnSelectedCandidate()
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

        var rerankService = new FixedLlmCandidateRerankService(new LlmCandidateRerankResult
        {
            SelectedSpecId = 2,
            Confidence = 0.93,
            Reason = "0.2cm 满足上限约束，0.7cm 不满足"
        });
        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 0.96,
            Reason = "候选 2 与源项约束语义一致"
        });

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmCandidateRerankService: rerankService,
            llmEquivalenceAdjudicationService: equivalenceService);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MinScoreThreshold = 0.0,
                RecallTopK = 2
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(2);
        result.Results[0].TopCandidates[0].SpecId.Should().Be(2);
        result.Results[0].SelectionMode.Should().Be(MatchSelectionMode.AiRerank);
        rerankService.Requests.Should().ContainSingle();
        rerankService.Requests[0].CurrentTopCandidateSpecId.Should().Be(1);
        equivalenceService.Requests.Should().ContainSingle();
        equivalenceService.Requests[0].CandidateSpecification.Should().Be("宽度等于0.2cm");
    }

    [Fact]
    public async Task BatchMatch_WhenExactTextShortcutHits_ShouldSkipAiRerank()
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
                SpecId = 1922,
                Project = "安装要求",
                Specification = "设备位置",
                Embedding = [1f]
            }
        };

        var rerankService = new FixedLlmCandidateRerankService(new LlmCandidateRerankResult
        {
            SelectedSpecId = 1922,
            Confidence = 0.99,
            Reason = "不会被调用"
        });

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f], defaultCandidateEmbedding: [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmCandidateRerankService: rerankService);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.95
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(1922);
        result.Results[0].SelectionMode.Should().Be(MatchSelectionMode.ExactShortcut);
        rerankService.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task BatchMatch_WhenAiRerankReturnsInvalidSpecId_ShouldFallbackToLocalTop1()
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

        var rerankService = new FixedLlmCandidateRerankService(new LlmCandidateRerankResult
        {
            SelectedSpecId = 999,
            Confidence = 0.12,
            Reason = "非法候选"
        });
        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Different,
            ReasonType = LlmEquivalenceReasonType.SemanticDifference,
            Confidence = 0.95,
            Reason = "沿用本地 Top1 后仍需人工确认"
        });

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmCandidateRerankService: rerankService,
            llmEquivalenceAdjudicationService: equivalenceService);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MinScoreThreshold = 0.0,
                RecallTopK = 2
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(1);
        result.Results[0].SelectionMode.Should().Be(MatchSelectionMode.EmbeddingTop1);
        rerankService.Requests.Should().ContainSingle();
        equivalenceService.Requests.Should().ContainSingle();
        equivalenceService.Requests[0].CandidateSpecification.Should().Be("宽度等于0.7cm");
    }

    [Fact]
    public async Task BatchMatch_WhenNumericConflictExists_ShouldStillRunLlmEquivalenceAdjudication()
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

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Different,
            ReasonType = LlmEquivalenceReasonType.SemanticDifference,
            Confidence = 0.97,
            Reason = "候选宽度超过源项上限，不能视为同一验收语义"
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                AmbiguityMargin = 0.01
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(10);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].IsHighConfidence.Should().BeFalse();
        result.Results[0].LlmEquivalence.Should().NotBeNull();
        result.Results[0].LlmEquivalence!.Verdict.Should().Be(LlmEquivalenceVerdict.Different);
        equivalenceService.Requests.Should().ContainSingle();
        result.Results[0].ScoreDetails.Should().NotContainKey("HardConflictPenalty");
        result.Results[0].ScoreDetails.Should().NotContainKey("ConflictPenalty");
        result.Results[0].RerankSummary.Should().NotContain("存在硬冲突已降权");
        result.Results[0].RerankSummary.Should().NotContain("冲突词");
    }

    [Fact]
    public async Task BatchMatch_WhenDirectionDiffersWithoutBuiltInConflictPairs_ShouldRequireManualReview()
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

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                AmbiguityMargin = 0.01
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(88);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
    }

    [Fact]
    public async Task BatchMatch_WhenDirectionWordsDifferWithoutBuiltInAntonymRules_ShouldRequireManualReview()
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                AmbiguityMargin = 0.01
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(889);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
    }

    [Fact]
    public async Task BatchMatch_WhenProjectOnlySharesSplitKeywords_ShouldNotUseKeywordOverlapFallback()
    {
        var source = new MatchSource
        {
            Project = "正转 模式",
            Specification = "速度 100 mm/s"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 890,
                Project = "反转 模式",
                Specification = "速度 100 mm/s",
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                AmbiguityMargin = 0.01
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(890);
        result.Results[0].ScoreDetails["ProjectMatch"].Should().Be(0, "关键词 overlap 不应再把“正转/反转 模式”打成项目接近");
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98
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
    public async Task BatchMatch_WhenBrandAliasNeedsAiEquivalence_ShouldAutoApply()
    {
        var source = new MatchSource
        {
            Project = "Panasonic 设备",
            Specification = "品牌要求 Panasonic"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 1923,
                Project = "松下 设备",
                Specification = "品牌要求 松下",
                Embedding = [0.95f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 0.93,
            Reason = "Panasonic 与 松下属于同一品牌的中英文表达"
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(1923);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].LlmEquivalence.Should().NotBeNull();
        result.Results[0].LlmEquivalence!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        equivalenceService.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task BatchMatch_WhenBrandConflictNeedsAiEquivalence_ShouldRequireManualReview()
    {
        var source = new MatchSource
        {
            Project = "Panasonic 设备",
            Specification = "品牌要求 Panasonic"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 1924,
                Project = "Mitsubishi 设备",
                Specification = "品牌要求 Mitsubishi",
                Embedding = [0.98f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Different,
            ReasonType = LlmEquivalenceReasonType.SemanticDifference,
            Confidence = 0.94,
            Reason = "Panasonic 与 Mitsubishi 不是同一品牌"
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(1924);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].LlmEquivalence.Should().NotBeNull();
        result.Results[0].LlmEquivalence!.Verdict.Should().Be(LlmEquivalenceVerdict.Different);
        equivalenceService.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task BatchMatch_WhenTextDiffAboveHighConfidenceThreshold_ShouldStillRunLlmEquivalenceAdjudication()
    {
        var source = new MatchSource
        {
            Project = "安装要求",
            Specification = "安装位置保持水平"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 1921,
                Project = "安装要求",
                Specification = "安装位置保持水平。",
                Embedding = [0.99f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.PunctuationOnly,
            Confidence = 0.95,
            Reason = "仅句号差异"
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.90
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(1921);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].LlmEquivalence.Should().NotBeNull();
        equivalenceService.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task BatchMatch_WhenTextIsExactlySame_ShouldAutoApplyViaExactShortcut()
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
                SpecId = 1922,
                Project = "安装要求",
                Specification = "设备位置",
                Embedding = [1f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.FormatOnly,
            Confidence = 0.99,
            Reason = "项目与规格文本完全一致"
        });

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f], defaultCandidateEmbedding: [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: equivalenceService);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.95
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(1922);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].LlmEquivalence.Should().NotBeNull();
        result.Results[0].LlmEquivalence!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        result.Results[0].LlmEquivalence!.Reason.Should().Be("项目与规格文本完全一致，已直接视为等价");
        equivalenceService.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task BatchMatch_WhenShortAsciiProjectAndSpecificationExactlyMatch_ShouldAutoApplyViaExactShortcut()
    {
        var source = new MatchSource
        {
            Project = "PA",
            Specification = "SA"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 2101,
                Project = "PA",
                Specification = "SA",
                Acceptance = "FILL-A",
                Remark = "REM-A"
            },
            new()
            {
                SpecId = 2102,
                Project = "PB",
                Specification = "SB",
                Acceptance = "FILL-B",
                Remark = "REM-B"
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.FormatOnly,
            Confidence = 0.99,
            Reason = "项目与规格文本完全一致"
        });

        var service = new SemanticKernelMatchingService(
            new ShortAsciiBucketEmbeddingService(),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: equivalenceService);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MinScoreThreshold = 0.0,
                RecallTopK = 2,
                HighConfidenceThreshold = 0.95
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(2101);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].Score.Should().Be(1.0);
        result.Results[0].LlmEquivalence.Should().NotBeNull();
        result.Results[0].LlmEquivalence!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        result.Results[0].LlmEquivalence!.Reason.Should().Be("项目与规格文本完全一致，已直接视为等价");
        equivalenceService.Requests.Should().BeEmpty();
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
                MinScoreThreshold = 0.0,
                RecallTopK = 2,
                AmbiguityMargin = 0.02,
                HighConfidenceThreshold = 0.98
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        var exceptionFallbackResult = result.Results[0].LlmEquivalence;
        exceptionFallbackResult.Should().NotBeNull();
        exceptionFallbackResult!.Verdict.Should().Be(LlmEquivalenceVerdict.Uncertain);
        exceptionFallbackResult.Reason.Should().Contain("裁决失败");
    }

    [Fact]
    public async Task BatchMatch_WhenIdentifierConflictExists_ShouldStillRunLlmEquivalenceAdjudication()
    {
        var source = new MatchSource
        {
            Project = "设备型号 ABC-100",
            Specification = "请使用 ABC-100"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 291,
                Project = "设备型号 ABC-700",
                Specification = "请使用 ABC-700",
                Acceptance = "RISKY",
                Embedding = [0.99f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Different,
            ReasonType = LlmEquivalenceReasonType.SemanticDifference,
            Confidence = 0.96,
            Reason = "型号不同，不能视为同一验收项"
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "identifier_conflict" &&
            issue.SourceValue == "ABC-100" &&
            issue.CandidateValue == "ABC-700");
        equivalenceService.Requests.Should().ContainSingle("型号冲突也应进入 AI 等价裁决");
    }

    [Fact]
    public async Task BatchMatch_WhenNumericFragmentsDifferWithoutStructuredConstraint_ShouldNotEmitLocalNumericIssue()
    {
        var source = new MatchSource
        {
            Project = "安装要求",
            Specification = "最大不可拆部件3200，预留空间200"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 292,
                Project = "安装要求",
                Specification = "最大不可拆部件3500，预留空间200",
                Embedding = [0.97f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 0.91,
            Reason = "3500 与 3200 在当前文本语境下属于描述口径差异，整体仍可视为同一验收项"
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].Issues.Should().BeEmpty();
        equivalenceService.Requests.Should().ContainSingle();
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
                new MatchSource { Project = "项目A", Specification = "规格A-源" },
                new MatchSource { Project = "项目B", Specification = "规格B-源" }
            ],
            [
                new MatchCandidate { SpecId = 1, Project = "项目A", Specification = "规格A-候选" }
            ],
            new MatchingConfig
            {
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                AmbiguityMargin = 0.01
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(601);
        result.Results[0].ScoreDetails["SpecificationText"].Should().Be(1.0);
    }

    [Fact]
    public async Task BatchMatch_WhenLaterNumericConstraintConflicts_ShouldNoLongerEmitLocalNumericEvidence()
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Evidence.NumericConstraints.Should().BeEmpty();
        result.Results[0].Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task BatchMatch_WhenVoltageAlternativeContainsDigitLoss_ShouldNotExposeLocalNumericConflict()
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.95
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Evidence.NumericConstraints.Should().BeEmpty();
        result.Results[0].Evidence.Conflicts.Should().BeEmpty();
        result.Results[0].Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task BatchMatch_WhenVoltageDecimalPointMismatch_ShouldRequireManualReviewWithoutStructuredNumericIssue()
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task BatchMatch_WhenCurrentDecimalPointMismatch_ShouldRequireManualReviewWithoutStructuredNumericIssue()
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task BatchMatch_WhenLaterIdentifierConflicts_ShouldRequireManualReview()
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Evidence.Identifiers.Should().HaveCount(2);
        result.Results[0].Evidence.Conflicts.Should().Contain(item => item.Contains("XYZ-200", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BatchMatch_WhenComparableTextOnlyDiffersByWhitespaceAndFullWidthBrackets_ShouldAutoApplyViaExactShortcut()
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.95
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(801);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].Score.Should().Be(1.0);
        result.Results[0].ScoreDetails["ProjectMatch"].Should().Be(1.0);
        result.Results[0].ScoreDetails["SpecificationText"].Should().Be(1.0);
    }

    [Fact]
    public async Task BatchMatch_WhenSpecificationIsExactlySameButNoKeywordToken_ShouldAutoApplyViaExactShortcut()
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
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(802);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].Score.Should().Be(1.0);
        result.Results[0].ScoreDetails.Should().NotContainKey("KeywordOverlap");
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

    private sealed class FixedLlmCandidateRerankService : ILlmCandidateRerankService
    {
        private readonly LlmCandidateRerankResult? _result;

        public FixedLlmCandidateRerankService(LlmCandidateRerankResult? result)
        {
            _result = result;
        }

        public List<LlmCandidateRerankRequest> Requests { get; } = [];

        public Task<LlmCandidateRerankResult?> RerankAsync(
            LlmCandidateRerankRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_result);
        }

        public bool TryParseRerankResult(string raw, out LlmCandidateRerankResult result)
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

    private sealed class ShortAsciiBucketEmbeddingService : IEmbeddingService
    {
        public bool IsAvailable => true;

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateVector(text));
        }

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(texts.Select(CreateVector).ToList());
        }

        public double ComputeSimilarity(float[] embedding1, float[] embedding2)
        {
            if (embedding1.Length == 0 || embedding2.Length == 0 || embedding1.Length != embedding2.Length)
            {
                return 0;
            }

            double dot = 0;
            double norm1 = 0;
            double norm2 = 0;
            for (var i = 0; i < embedding1.Length; i++)
            {
                dot += embedding1[i] * embedding2[i];
                norm1 += embedding1[i] * embedding1[i];
                norm2 += embedding2[i] * embedding2[i];
            }

            if (norm1 <= 0 || norm2 <= 0)
            {
                return 0;
            }

            return Math.Clamp(dot / (Math.Sqrt(norm1) * Math.Sqrt(norm2)), 0, 1);
        }

        private static float[] CreateVector(string text)
        {
            var value = text ?? string.Empty;
            var vector = new float[16];

            for (var i = 0; i < value.Length; i++)
            {
                var bucket = i % vector.Length;
                vector[bucket] += value[i];
            }

            var norm = (float)Math.Sqrt(vector.Sum(v => v * v));
            if (norm <= 0)
            {
                return vector;
            }

            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }

            return vector;
        }
    }
}
