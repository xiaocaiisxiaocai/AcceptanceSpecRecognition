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
    public async Task BatchMatch_WhenHighScoreNoConflictAndNoLlm_ShouldDeterministicAutoApply()
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
        // 新架构（A选项）：无结构化冲突 + Embedding 达到高置信阈值 + 不歧义 → 确定性自动通过，不再依赖 LLM 等价裁决
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].IsHighConfidence.Should().BeTrue();
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
        // 旧架构：不产出任何本地数值证据，把"0.7 是否满足 <0.5"留给 LLM；
        // 新架构：确定性数值冲突层主动检出 0.7≠0.5（维持严格），产出 hard_conflict 强制人工。
        // NumericConstraints 仍为空（新层走 Issues 而非旧的 NumericConstraints 列表）。
        result.Results[0].Evidence.NumericConstraints.Should().BeEmpty();
        result.Results[0].Issues.Should().Contain(issue => issue.Code == "numeric_unit_conflict");
    }

    [Fact]
    public async Task BatchMatch_WhenAiRerankSelectsLowerEmbeddingCandidate_ShouldPromoteItAndRunEquivalenceOnSelectedCandidate()
    {
        // 用纯文本场景验证"rerank 改选 → 对改选后候选跑等价裁决"这条链路。
        // 不含数值/单位/比较符/反义，避免确定性硬冲突短路掉等价裁决，保住本测试的原始意图。
        var source = new MatchSource
        {
            Project = "安装要求",
            Specification = "设备需稳固安装在底座"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 1,
                Project = "安装要求",
                Specification = "设备建议安装在底座附近",
                Acceptance = "RISKY",
                Embedding = [0.99f]
            },
            new()
            {
                SpecId = 2,
                Project = "安装要求",
                Specification = "设备应稳固安装于底座",
                Acceptance = "SAFE",
                Embedding = [0.90f]
            }
        };

        var rerankService = new FixedLlmCandidateRerankService(new LlmCandidateRerankResult
        {
            SelectedSpecId = 2,
            Confidence = 0.93,
            Reason = "候选 2 表述更贴近源项的稳固安装语义"
        });
        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 0.96,
            Reason = "候选 2 与源项语义一致"
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
                RecallTopK = 2,
                EnableLlmEquivalenceAdjudication = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(2);
        result.Results[0].TopCandidates[0].SpecId.Should().Be(2);
        result.Results[0].SelectionMode.Should().Be(MatchSelectionMode.AiRerank);
        rerankService.Requests.Should().ContainSingle();
        rerankService.Requests[0].CurrentTopCandidateSpecId.Should().Be(1);
        equivalenceService.Requests.Should().ContainSingle();
        equivalenceService.Requests[0].CandidateSpecification.Should().Be("设备应稳固安装于底座");
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
            Project = "安装要求",
            Specification = "设备需稳固安装在底座"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 1,
                Project = "安装要求",
                Specification = "设备建议安装在底座附近",
                Acceptance = "RISKY",
                Embedding = [0.99f]
            },
            new()
            {
                SpecId = 2,
                Project = "安装要求",
                Specification = "设备应稳固安装于底座",
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
                RecallTopK = 2,
                EnableLlmEquivalenceAdjudication = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(1);
        result.Results[0].SelectionMode.Should().Be(MatchSelectionMode.EmbeddingTop1);
        rerankService.Requests.Should().ContainSingle();
        equivalenceService.Requests.Should().ContainSingle();
        equivalenceService.Requests[0].CandidateSpecification.Should().Be("设备建议安装在底座附近");
    }

    [Fact]
    public async Task BatchMatch_WhenSpecificationOnlyExactMatchHasMultipleCandidates_ShouldRequireManualReview()
    {
        var source = new MatchSource
        {
            Project = "客户文档项目",
            Specification = "安全门闭合后允许启动"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 10,
                Project = "历史项目A",
                Specification = "安全门闭合后允许启动",
                Acceptance = "验收A",
                Embedding = [1f]
            },
            new()
            {
                SpecId = 11,
                Project = "历史项目B",
                Specification = "安全门闭合后允许启动",
                Acceptance = "验收B",
                Embedding = [1f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new ThrowingEmbeddingService(),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingMode = MatchingMode.SpecificationOnly,
                RecallTopK = 3
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].IsAmbiguous.Should().BeTrue();
        result.Results[0].MatchBasis.Should().Be(MatchBasis.Specification);
        result.Results[0].RecalledCandidateCount.Should().Be(2);
        result.Results[0].TopCandidates.Should().HaveCount(2);
    }

    [Fact]
    public async Task BatchMatch_WhenNumericConflictExists_ShouldShortCircuitToManualReviewWithoutLlm()
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
                AmbiguityMargin = 0.01,
                EnableLlmEquivalenceAdjudication = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(10);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].IsHighConfidence.Should().BeFalse();
        // 新架构（维持严格）：源含约束上限 0.5 与候选取值 0.7 数值不等→确定性 hard_conflict 短路，
        // 直接转人工，不再调用 LLM 等价裁决；冲突原因以结构化 issue 给出。
        result.Results[0].LlmEquivalence.Should().BeNull();
        equivalenceService.Requests.Should().BeEmpty();
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "numeric_unit_conflict" && issue.Severity == "hard_conflict");
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
                HighConfidenceThreshold = 0.98,
                EnableLlmEquivalenceAdjudication = true
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
    public async Task BatchMatch_WhenBrandAliasResolvedByCanonicalization_ShouldAutoApplyWithoutLlm()
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
                HighConfidenceThreshold = 0.98,
                EnableLlmEquivalenceAdjudication = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(1923);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].LlmEquivalence.Should().NotBeNull();
        result.Results[0].LlmEquivalence!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        // 新架构：松下/Panasonic 由品牌字典确定性归一→规范化精确命中（ExactShortcut），不再调用 LLM 等价裁决
        result.Results[0].SelectionMode.Should().Be(MatchSelectionMode.ExactShortcut);
        equivalenceService.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task BatchMatch_WhenWrongProjectCodeCandidateHasExactSpec_ShouldPreferExactProjectAliasCandidate()
    {
        var source = new MatchSource
        {
            Project = "接触器品牌要求 B017",
            Specification = "接触器品牌为 Siemens，型号 3RT2025"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 324,
                Project = "接触器品牌要求 B007",
                Specification = "接触器品牌为 Siemens，型号 3RT2025",
                Acceptance = "WRONG-PROJECT",
                Embedding = [0.9353f]
            },
            new()
            {
                SpecId = 334,
                Project = "接触器品牌要求 B017",
                Specification = "选用 Siemens 接触器，型号 3RT2025",
                Acceptance = "RIGHT-PROJECT",
                Embedding = [0.9901f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 0.95,
            Reason = "Siemens 与 西门子属于同一品牌的中英文表达"
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
                MinScoreThreshold = 0.9,
                RecallTopK = 2,
                HighConfidenceThreshold = 0.98,
                EnableLlmEquivalenceAdjudication = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(334);
        result.Results[0].MatchedAcceptance.Should().Be("RIGHT-PROJECT");
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].TopCandidates.Select(candidate => candidate.SpecId)
            .Should()
            .ContainInOrder(334, 324);
        result.Results[0].TopCandidates[0].ScoreDetails["ProjectMatch"].Should().Be(1.0);
    }

    [Fact]
    public async Task BatchMatch_WhenProjectConflictCandidateFallsClearlyBehind_ShouldSkipAiRerankAndKeepLocalExactProjectTop1()
    {
        var source = new MatchSource
        {
            Project = "接触器品牌要求 B017",
            Specification = "接触器品牌为 Siemens，型号 3RT2025"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 324,
                Project = "接触器品牌要求 B007",
                Specification = "接触器品牌为 Siemens，型号 3RT2025",
                Acceptance = "WRONG-PROJECT",
                Embedding = [0.9353f]
            },
            new()
            {
                SpecId = 334,
                Project = "接触器品牌要求 B017",
                Specification = "选用 Siemens 接触器，型号 3RT2025",
                Acceptance = "RIGHT-PROJECT",
                Embedding = [0.9901f]
            }
        };

        var rerankService = new FixedLlmCandidateRerankService(new LlmCandidateRerankResult
        {
            SelectedSpecId = 324,
            Confidence = 0.86,
            Reason = "规格字面完全一致"
        });
        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 0.95,
            Reason = "Siemens 与 西门子属于同一品牌的中英文表达"
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
                MinScoreThreshold = 0.9,
                RecallTopK = 2,
                HighConfidenceThreshold = 0.98
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(334);
        result.Results[0].MatchedAcceptance.Should().Be("RIGHT-PROJECT");
        result.Results[0].SelectionMode.Should().Be(MatchSelectionMode.EmbeddingTop1);
        rerankService.Requests.Should().BeEmpty("项目编码冲突候选明显落后时不应进入 AI 候选重排");
    }

    [Fact]
    public async Task BatchMatch_WhenLocalTop1IsExactProjectAndClearlyAhead_ShouldSkipAiRerank()
    {
        var source = new MatchSource
        {
            Project = "接触器品牌要求 B017",
            Specification = "接触器品牌为 Siemens，型号 3RT2025"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 324,
                Project = "接触器品牌要求 B007",
                Specification = "接触器品牌为 Siemens，型号 3RT2025",
                Acceptance = "WRONG-PROJECT",
                Embedding = [0.9353f]
            },
            new()
            {
                SpecId = 334,
                Project = "接触器品牌要求 B017",
                Specification = "选用 Siemens 接触器，型号 3RT2025",
                Acceptance = "RIGHT-PROJECT",
                Embedding = [0.9901f]
            }
        };

        var rerankService = new FixedLlmCandidateRerankService(new LlmCandidateRerankResult
        {
            SelectedSpecId = 324,
            Confidence = 0.86,
            Reason = "不应触发"
        });
        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 0.95,
            Reason = "Siemens 与 西门子属于同一品牌的中英文表达"
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
                MinScoreThreshold = 0.9,
                RecallTopK = 2,
                HighConfidenceThreshold = 0.98,
                AmbiguityMargin = 0.02,
                EnableLlmEquivalenceAdjudication = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(334);
        result.Results[0].MatchedAcceptance.Should().Be("RIGHT-PROJECT");
        result.Results[0].SelectionMode.Should().Be(MatchSelectionMode.EmbeddingTop1);
        rerankService.Requests.Should().BeEmpty("本地 Top1 项目精确命中且分差明确时不应进入 AI 候选重排");
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
                HighConfidenceThreshold = 0.98,
                EnableLlmEquivalenceAdjudication = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(1924);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].LlmEquivalence.Should().NotBeNull();
        result.Results[0].LlmEquivalence!.Verdict.Should().Be(LlmEquivalenceVerdict.Different);
        equivalenceService.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(9101, "输送方向 S001", "板件从左侧进板、从右侧出板", "输送方向为左进右出")]
    [InlineData(9130, "待机逻辑 S030", "若10分钟内没有来板，设备应自动切换到待机", "连续10分钟无板时自动进入待机")]
    public async Task BatchMatch_WhenProjectExactAndSemanticPositiveFallsBelowThreshold_ShouldStillEnterAiEquivalence(
        int specId,
        string project,
        string sourceSpecification,
        string candidateSpecification)
    {
        var source = new MatchSource
        {
            Project = project,
            Specification = sourceSpecification
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = specId,
                Project = project,
                Specification = candidateSpecification,
                Acceptance = $"ACC-{specId}",
                Embedding = [0.88f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 0.92,
            Reason = "两段文本属于同一验收语义的不同表达"
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
                MinScoreThreshold = 0.9,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98,
                EnableLlmEquivalenceAdjudication = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(specId);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].IsHighConfidence.Should().BeTrue();
        equivalenceService.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task BatchMatch_WhenTextDiffAboveHighConfidenceWithoutConflict_ShouldDeterministicAutoApplyWithoutLlm()
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
                HighConfidenceThreshold = 0.90,
                EnableLlmEquivalenceAdjudication = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(1921);
        // 新架构：仅句号差异、无任何结构化冲突且 Embedding 达到高置信阈值
        // → 确定性自动通过（A 选项），不再调用 LLM 等价裁决
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].LlmEquivalence.Should().NotBeNull();
        equivalenceService.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task BatchMatch_WhenBestCandidateOnlyPassesLegacyMediumThreshold_ShouldSkipLlmEquivalenceAdjudication()
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
                SpecId = 1923,
                Project = "安装要求",
                Specification = "安装位置应保持水平",
                Embedding = [0.89f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 0.95,
            Reason = "测试桩：不应触发"
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
                MinScoreThreshold = 0.90,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98,
                EnableLlmEquivalenceAdjudication = false
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(1923);
        result.Results[0].Score.Should().BeGreaterThan(MatchingThresholds.MediumConfidenceScore);
        result.Results[0].Score.Should().BeLessThan(0.90);
        result.Results[0].LlmEquivalence.Should().BeNull();
        equivalenceService.Requests.Should().BeEmpty();
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
                HighConfidenceThreshold = 0.98,
                EnableLlmEquivalenceAdjudication = true
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
                HighConfidenceThreshold = 0.98,
                EnableLlmEquivalenceAdjudication = true
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(193);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].IsHighConfidence.Should().BeFalse();
        result.Results[0].LlmEquivalence.Should().NotBeNull();
        result.Results[0].LlmEquivalence!.Verdict.Should().Be(LlmEquivalenceVerdict.Uncertain);
    }

    [Fact]
    public async Task BatchMatch_WhenLlmEquivalenceAdjudicationDisabled_ShouldNotCallLlm()
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
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 0.99,
            Reason = "应被配置关闭"
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
                HighConfidenceThreshold = 0.98,
                EnableLlmEquivalenceAdjudication = false
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(193);
        result.Results[0].LlmEquivalence.Should().BeNull();
        equivalenceService.Requests.Should().BeEmpty();
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
                HighConfidenceThreshold = 0.98,
                EnableLlmEquivalenceAdjudication = true
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
                HighConfidenceThreshold = 0.98,
                EnableLlmEquivalenceAdjudication = true
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
                HighConfidenceThreshold = 0.98,
                EnableLlmEquivalenceAdjudication = true
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
                RecallTopK = 1,
                EnableLlmEquivalenceAdjudication = true
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
    public async Task BatchMatch_WhenCompactModelIdentifiersDiffer_ShouldDetectIdentifierConflict()
    {
        var source = new MatchSource
        {
            Project = "轴承型号要求",
            Specification = "使用轴承 MK2530"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 294,
                Project = "轴承型号要求",
                Specification = "使用轴承 6204ZZ",
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
                HighConfidenceThreshold = 0.95
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "identifier_conflict" &&
            issue.SourceValue == "MK2530" &&
            issue.CandidateValue == "6204ZZ");
    }

    [Fact]
    public async Task BatchMatch_WhenSkeletonEqualButEmbeddingLow_ShouldRescueCandidateIntoView()
    {
        // 项目精确一致、规格"骨架"相同（去数值后结构一致），但 Embedding 仅 0.6（低于 0.90 召回阈值，
        // 也低于 0.70 语义等价救援阈值）。改动前该候选会被召回层直接丢弃 → 显示"无匹配"；
        // 骨架相似救援应把它救回视野，让审核员看到最接近的候选及其数值冲突。
        var source = new MatchSource
        {
            Project = "电机要求",
            Specification = "电机额定转速3000rpm"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 295,
                Project = "电机要求",
                Specification = "电机额定转速3500rpm",
                Embedding = [0.6f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f], defaultCandidateEmbedding: [0.6f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MinScoreThreshold = 0.90,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.95,
                EnableLlmEquivalenceAdjudication = false
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(295, "骨架相同的候选不应被召回层静默丢弃");
        // 数值不同（3000 vs 3500 rpm）构成硬冲突，仍转人工——救援只负责"召回进视野"，不放宽冲突门禁
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Issues.Should().Contain(issue => issue.Code == "numeric_unit_conflict");
    }

    [Fact]
    public async Task BatchMatch_WhenSkeletonDiffersAndEmbeddingLow_ShouldStillDrop()
    {
        // 骨架不同（结构不一致）且 Embedding 低：救援不应生效，维持"无匹配"，避免召回泛滥。
        var source = new MatchSource
        {
            Project = "电机要求",
            Specification = "电机额定转速3000rpm"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 296,
                Project = "电机要求",
                Specification = "防护等级IP65",
                Embedding = [0.6f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f], defaultCandidateEmbedding: [0.6f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MinScoreThreshold = 0.90,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.95,
                EnableLlmEquivalenceAdjudication = false
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().BeNull("骨架不同的低分候选不应被救援");
    }

    [Theory]
    [InlineData(0.7, MatchDecision.ManualReview)]
    [InlineData(0.9, MatchDecision.AutoApply)]
    public async Task BatchMatch_WhenIdentifierConflictAndLlmSaysEquivalent_ShouldRequireHigherConfidenceFloor(
        double llmConfidence,
        MatchDecision expectedDecision)
    {
        var source = new MatchSource
        {
            Project = "轴承型号要求",
            Specification = "使用轴承 SKF-6204-2Z"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 293,
                Project = "轴承型号要求",
                Specification = "使用轴承 SKF-6204-ZZ",
                Acceptance = "按轴承型号验收",
                Embedding = [0.99f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.FormatOnly,
            Confidence = llmConfidence,
            Reason = "2Z 与 ZZ 同为双面防尘盖标记，型号实质一致"
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
                EnableLlmEquivalenceAdjudication = true
            });

        // 型号/料号冲突行的 LLM Equivalent 结论必须满足更高置信度门槛（0.85）才能自动通过，
        // 防止单次 LLM 误判直接造成错填物料号。
        result.Results.Should().HaveCount(1);
        result.Results[0].Issues.Should().Contain(issue => issue.Code == "identifier_conflict");
        result.Results[0].Decision.Should().Be(expectedDecision);
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
                RecallTopK = 1,
                EnableLlmEquivalenceAdjudication = true
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
        // NumericConstraints 是旧的结构化约束证据，本架构不再使用，保持为空。
        result.Results[0].Evidence.NumericConstraints.Should().BeEmpty();
        // 新架构：高度 20mm vs 30mm 经确定性单位引擎判出数值不等价（length 量纲），产出 hard_conflict。
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "numeric_unit_conflict" && issue.Severity == "hard_conflict");
    }

    [Fact]
    public async Task BatchMatch_WhenVoltageAlternativeContainsDigitLoss_ShouldRequireManualReviewWithNumericConflictIssue()
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
        result.Results[0].Evidence.Conflicts.Should().ContainSingle(conflict =>
            conflict.Contains("22V vs 220V", StringComparison.OrdinalIgnoreCase));
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "numeric_unit_conflict" &&
            issue.Severity == "hard_conflict");
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
        // 新架构：24V vs 2.4V 由确定性单位引擎判出数值不等价（同 voltage 量纲），产出 hard_conflict，强制人工。
        // 旧架构无本地数值检测、靠 Embedding 分数侥幸，现在改为明确的结构化冲突，更准更可解释。
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "numeric_unit_conflict" && issue.Severity == "hard_conflict");
    }

    [Fact]
    public async Task BatchMatch_WhenCurrentDecimalPointMismatch_ShouldRequireManualReviewWithNumericConflictIssue()
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
        // 新架构：2A vs 0.2A 由确定性单位引擎判出数值不等价（同 current 量纲），产出 hard_conflict，强制人工。
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "numeric_unit_conflict" && issue.Severity == "hard_conflict");
    }

    [Fact]
    public async Task BatchMatch_WhenDimensionTupleWithUnitsSwapsPositions_ShouldRequireManualReview()
    {
        var source = new MatchSource
        {
            Project = "外形尺寸",
            Specification = "工装外形尺寸200mm×100mm"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 605,
                Project = "外形尺寸",
                Specification = "工装外形尺寸100mm×200mm",
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
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "dimension_tuple_conflict" &&
            issue.Severity == "hard_conflict");
    }

    [Fact]
    public async Task BatchMatch_WhenPercentValueDiffers_ShouldRequireManualReviewWithNumericConflictIssue()
    {
        var source = new MatchSource
        {
            Project = "良率要求",
            Specification = "不良率不超过1%"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 606,
                Project = "良率要求",
                Specification = "不良率不超过2%",
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
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "numeric_unit_conflict" &&
            issue.Severity == "hard_conflict");
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

    [Fact]
    public async Task BatchMatch_WhenAutomationBrandAndTorqueUnitAreKnownAliases_ShouldCanonicalShortcut()
    {
        var source = new MatchSource
        {
            Project = "ABB 变频器",
            Specification = "输出扭矩等于1.5N·m，转速不低于1200rpm"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 901,
                Project = "阿西布朗 变频器",
                Specification = "输出扭矩等于150N·cm，转速不低于1200r/min",
                Acceptance = "扭矩与转速满足要求",
                Embedding = [1f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new ThrowingEmbeddingService(),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98
            });

        result.Results.Should().ContainSingle();
        result.Results[0].MatchedSpecId.Should().Be(901);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].SelectionMode.Should().Be(MatchSelectionMode.ExactShortcut);
        result.Results[0].LlmEquivalence!.Reason.Should().Contain("规范化后等价");
    }

    [Fact]
    public async Task BatchMatch_WhenAutomationBrandAliasIsAdjacentToDeviceName_ShouldCanonicalShortcut()
    {
        var source = new MatchSource
        {
            Project = "ABB变频器",
            Specification = "额定功率等于7.5kW"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 908,
                Project = "阿西布朗变频器",
                Specification = "额定功率等于7500W",
                Acceptance = "功率满足要求",
                Embedding = [1f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new ThrowingEmbeddingService(),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98
            });

        result.Results.Should().ContainSingle();
        result.Results[0].MatchedSpecId.Should().Be(908);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].SelectionMode.Should().Be(MatchSelectionMode.ExactShortcut);
    }

    [Fact]
    public async Task BatchMatch_WhenExternalBrandAndUnitRulesMatch_ShouldCanonicalShortcut()
    {
        var source = new MatchSource
        {
            Project = "Mean Well开关电源",
            Specification = "冷却气流量等于30NL/min"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 909,
                Project = "明纬开关电源",
                Specification = "冷却气流量等于30SLM",
                Acceptance = "品牌和流量满足要求",
                Embedding = [1f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new ThrowingEmbeddingService(),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98
            });

        result.Results.Should().ContainSingle();
        result.Results[0].MatchedSpecId.Should().Be(909);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].SelectionMode.Should().Be(MatchSelectionMode.ExactShortcut);
    }

    [Fact]
    public async Task BatchMatch_WhenExternalBrandAliasMatches_ShouldNotEmitUnknownBrandWarning()
    {
        var source = new MatchSource
        {
            Project = "电源品牌要求",
            Specification = "品牌要求 Mean Well，电压等于24V"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 910,
                Project = "电源品牌要求",
                Specification = "品牌要求 明纬，电压等于24V",
                Acceptance = "品牌一致",
                Embedding = [1f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new ThrowingEmbeddingService(),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98
            });

        result.Results.Should().ContainSingle();
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].Issues.Should().NotContain(issue => issue.Code == "unknown_brand_token");
    }

    [Fact]
    public async Task BatchMatch_WhenDifferentExternalBrands_ShouldKeepUnknownBrandGate()
    {
        var source = new MatchSource
        {
            Project = "交换机品牌要求",
            Specification = "品牌要求 Moxa，端口数等于8"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 911,
                Project = "交换机品牌要求",
                Specification = "品牌要求 Hirschmann，端口数等于8",
                Acceptance = "品牌一致",
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
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98,
                EnableDeterministicAutoApply = true,
                EnableLlmEquivalenceAdjudication = false
            });

        result.Results.Should().ContainSingle();
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "unknown_brand_token" &&
            issue.Severity == "warning");
    }

    [Fact]
    public async Task BatchMatch_WhenBoundedRangeUsesSameComparators_ShouldNotEmitComparatorConflict()
    {
        var source = new MatchSource
        {
            Project = "气压范围",
            Specification = "工作气压不低于0.4MPa且不超过0.6MPa"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 904,
                Project = "气压范围",
                Specification = "工作气压不低于0.4MPa且不超过0.6MPa",
                Acceptance = "气压范围满足要求",
                Embedding = [1f]
            }
        };

        var service = new SemanticKernelMatchingService(
            new ThrowingEmbeddingService(),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98
            });

        result.Results.Should().ContainSingle();
        result.Results[0].MatchedSpecId.Should().Be(904);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].Issues.Should().NotContain(issue => issue.Code == "comparator_conflict");
    }

    [Fact]
    public async Task BatchMatch_WhenSingleComparatorDirectionIsOpposite_ShouldRequireManualReview()
    {
        var source = new MatchSource
        {
            Project = "气压下限",
            Specification = "工作气压不低于0.4MPa"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 905,
                Project = "气压下限",
                Specification = "工作气压不超过0.4MPa",
                Acceptance = "方向相反",
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
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98,
                EnableDeterministicAutoApply = true,
                EnableLlmEquivalenceAdjudication = false
            });

        result.Results.Should().ContainSingle();
        result.Results[0].MatchedSpecId.Should().Be(905);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "comparator_conflict" &&
            issue.Severity == "hard_conflict");
    }

    [Fact]
    public async Task BatchMatch_WhenUnknownUnitDiffers_ShouldBlockDeterministicAutoApply()
    {
        var source = new MatchSource
        {
            Project = "传感器采样要求",
            Specification = "采样周期等于5fooRate"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 902,
                Project = "传感器采样要求",
                Specification = "采样周期等于5barRate",
                Acceptance = "按采样周期验收",
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
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98,
                EnableDeterministicAutoApply = true,
                EnableLlmEquivalenceAdjudication = false
            });

        result.Results.Should().ContainSingle();
        result.Results[0].MatchedSpecId.Should().Be(902);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "unknown_unit_token" &&
            issue.Severity == "warning");
        result.Results[0].LlmEquivalence.Should().BeNull();
    }

    [Fact]
    public async Task BatchMatch_WhenUnknownUnitExistsOnlyOnOneSide_ShouldBlockDeterministicAutoApply()
    {
        var source = new MatchSource
        {
            Project = "传感器采样要求",
            Specification = "采样周期等于5fooRate"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 906,
                Project = "传感器采样要求",
                Specification = "采样周期等于5",
                Acceptance = "按采样周期验收",
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
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98,
                EnableDeterministicAutoApply = true,
                EnableLlmEquivalenceAdjudication = false
            });

        result.Results.Should().ContainSingle();
        result.Results[0].MatchedSpecId.Should().Be(906);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "unknown_unit_token" &&
            issue.Severity == "warning");
    }

    [Fact]
    public async Task BatchMatch_WhenUnknownUnitExistsOnlyOnOneSideAndLlmSaysEquivalent_ShouldAutoApply()
    {
        var source = new MatchSource
        {
            Project = "单位单侧未知B 用例169",
            Specification = "扫码时间不超过0.5sec/次"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 916,
                Project = "单位单侧未知B 用例169",
                Specification = "扫码时间不超过500ms",
                Acceptance = "按扫码时间验收",
                Embedding = [1f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.SymbolEquivalent,
            Confidence = 0.95,
            Reason = "0.5 秒与 500ms 等价"
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
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98,
                EnableDeterministicAutoApply = true,
                EnableLlmEquivalenceAdjudication = true,
                LlmMaxCallsPerBatch = 3
            });

        // 未知单位 warning 行本就是 LLM 擅长的灰区：LLM 已高置信判定等价时直接采纳，
        // 不再先于 LLM 结论转人工（warning 仍然阻断"确定性自动通过"路径）。
        result.Results.Should().ContainSingle();
        result.Results[0].MatchedSpecId.Should().Be(916);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "unknown_unit_token" &&
            issue.Severity == "warning");
        result.Results[0].LlmEquivalence!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        equivalenceService.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task BatchMatch_WhenUnknownUnitExistsAndLlmEquivalentConfidenceIsLow_ShouldRequireManualReview()
    {
        var source = new MatchSource
        {
            Project = "单位单侧未知B 用例169",
            Specification = "扫码时间不超过0.5sec/次"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 916,
                Project = "单位单侧未知B 用例169",
                Specification = "扫码时间不超过500ms",
                Acceptance = "按扫码时间验收",
                Embedding = [1f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.SymbolEquivalent,
            Confidence = 0.3,
            Reason = "倾向等价但不确定"
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
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98,
                EnableDeterministicAutoApply = true,
                EnableLlmEquivalenceAdjudication = true,
                LlmMaxCallsPerBatch = 3
            });

        result.Results.Should().ContainSingle();
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
    }

    [Theory]
    [InlineData("复合范围未覆盖E", "工件间距≤20mm", "工件间距不多于二十毫米")]
    [InlineData("自然语言范围", "升降行程≈0.5m", "升降行程约半米")]
    [InlineData("复合范围未覆盖F", "扫码成功率≥99.9%", "扫码成功率99.9%以上")]
    [InlineData("复合范围未覆盖B", "电源波动范围上下浮动10%", "电源波动范围±10%")]
    public async Task BatchMatch_WhenUnsupportedFormatExistsAndLlmSaysEquivalent_ShouldAutoApply(
        string project,
        string sourceSpecification,
        string candidateSpecification)
    {
        var source = new MatchSource
        {
            Project = project,
            Specification = sourceSpecification
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 917,
                Project = project,
                Specification = candidateSpecification,
                Acceptance = "按格式门禁验收",
                Embedding = [1f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 0.95,
            Reason = "LLM 认为语义等价"
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
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98,
                EnableDeterministicAutoApply = true,
                EnableLlmEquivalenceAdjudication = true,
                LlmMaxCallsPerBatch = 3
            });

        // 规则未覆盖的自然语言/中文数字格式正是 LLM 的判读强项：
        // LLM 已高置信判定等价时直接采纳（warning 仍然阻断"确定性自动通过"路径）。
        result.Results.Should().ContainSingle();
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "unsupported_format_token" &&
            issue.Severity == "warning");
        result.Results[0].LlmEquivalence!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
    }

    [Fact]
    public async Task BatchMatch_WhenUnknownBrandDiffers_ShouldUseLlmInsteadOfDeterministicAutoApply()
    {
        var source = new MatchSource
        {
            Project = "视觉品牌要求",
            Specification = "品牌要求 AcmeVision，分辨率等于500万像素"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 903,
                Project = "视觉品牌要求",
                Specification = "品牌要求 BetaVision，分辨率等于500万像素",
                Acceptance = "按视觉品牌验收",
                Embedding = [1f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Uncertain,
            ReasonType = LlmEquivalenceReasonType.Uncertain,
            Confidence = 0.4,
            Reason = "品牌不在确定性别名字典中，需人工确认"
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
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98,
                EnableDeterministicAutoApply = true,
                EnableLlmEquivalenceAdjudication = true,
                LlmMaxCallsPerBatch = 3
            });

        result.Results.Should().ContainSingle();
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "unknown_brand_token" &&
            issue.Severity == "warning");
        equivalenceService.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task BatchMatch_WhenChineseAutomationBrandDiffers_ShouldUseLlmInsteadOfDeterministicAutoApply()
    {
        var source = new MatchSource
        {
            Project = "伺服品牌要求",
            Specification = "品牌要求 汇川，功率等于750W"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 907,
                Project = "伺服品牌要求",
                Specification = "品牌要求 英威腾，功率等于750W",
                Acceptance = "按伺服品牌验收",
                Embedding = [1f]
            }
        };

        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Uncertain,
            ReasonType = LlmEquivalenceReasonType.Uncertain,
            Confidence = 0.4,
            Reason = "品牌不同，需人工确认"
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
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98,
                EnableDeterministicAutoApply = true,
                EnableLlmEquivalenceAdjudication = true,
                LlmMaxCallsPerBatch = 3
            });

        result.Results.Should().ContainSingle();
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "unknown_brand_token" &&
            issue.Severity == "warning");
        equivalenceService.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task BatchMatch_WhenRangeConnectorUsesChineseAndEnglish_ShouldTreatSpecificationAsEquivalent()
    {
        var source = new MatchSource
        {
            Project = "基板厚度",
            Specification = "0.03mm 到2.0mm. 常用0.04mm 到 2.0mm."
        };
        var candidate = new MatchCandidate
        {
            SpecId = 9920,
            Project = "基板厚度",
            Specification = "0.03mm to 2.0mm. 常用 0.04mm to 2.0mm.",
            Acceptance = "NG",
            Remark = "0.05不含铜-2mm 板弯翘±15mm内",
            Embedding = [1f]
        };
        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Different,
            ReasonType = LlmEquivalenceReasonType.SemanticDifference,
            Confidence = 0.9,
            Reason = "候选新增了验收标准和备注约束"
        });
        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [0.982f], defaultCandidateEmbedding: [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: equivalenceService);

        var result = await service.BatchMatchAsync(
            [source],
            [candidate],
            new MatchingConfig
            {
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.95,
                EnableDeterministicAutoApply = true,
                EnableLlmEquivalenceAdjudication = true
            });

        var match = result.Results.Should().ContainSingle().Subject;
        match.MatchedSpecId.Should().Be(9920);
        match.ScoreDetails["SpecificationText"].Should().Be(1);
        match.Score.Should().BeGreaterThan(0.95);
        match.Decision.Should().Be(MatchDecision.AutoApply);
        equivalenceService.Requests.Should().BeEmpty(
            "中英文区间连接词归一化后已达到确定性高置信，不应再交给 AI 误判");
    }

    [Fact]
    public async Task BatchMatch_WhenAiRejectsSameNormalizedKeyBecauseOfAuxiliaryFields_ShouldCorrectToEquivalent()
    {
        var source = new MatchSource
        {
            Project = "基板厚度",
            Specification = "0.03mm 到2.0mm"
        };
        var candidate = new MatchCandidate
        {
            SpecId = 9920,
            Project = "基板厚度",
            Specification = "0.03mm to 2.0mm",
            Acceptance = "NG",
            Remark = "候选备注仅用于回填",
            Embedding = [1f]
        };
        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Different,
            ReasonType = LlmEquivalenceReasonType.SemanticDifference,
            Confidence = 0.95,
            Reason = "候选存在源项没有提供的验收标准和备注"
        });
        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [0.9f], defaultCandidateEmbedding: [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: equivalenceService);

        var result = await service.BatchMatchAsync(
            [source],
            [candidate],
            new MatchingConfig
            {
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.95,
                EnableDeterministicAutoApply = false,
                EnableLlmEquivalenceAdjudication = true
            });

        var match = result.Results.Should().ContainSingle().Subject;
        equivalenceService.Requests.Should().ContainSingle();
        match.LlmEquivalence!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        match.LlmEquivalence.Reason.Should().Contain("仅作为回填上下文");
        match.Decision.Should().Be(MatchDecision.AutoApply);
    }

    [Fact]
    public async Task BatchMatch_WhenRangeBoundaryDiffers_ShouldKeepHardConflictManualReview()
    {
        var source = new MatchSource
        {
            Project = "基板厚度",
            Specification = "0.03mm 到2.0mm"
        };
        var candidate = new MatchCandidate
        {
            SpecId = 9921,
            Project = "基板厚度",
            Specification = "0.03mm to 2.1mm",
            Acceptance = "NG",
            Remark = "候选备注",
            Embedding = [1f]
        };
        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [0.98f], defaultCandidateEmbedding: [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            [candidate],
            new MatchingConfig
            {
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.95
            });

        var match = result.Results.Should().ContainSingle().Subject;
        match.Decision.Should().Be(MatchDecision.ManualReview);
        match.Issues.Should().Contain(issue => issue.Code == "numeric_unit_conflict");
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

    private sealed class ThrowingEmbeddingService : IEmbeddingService
    {
        public bool IsAvailable => true;

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("精确直达路径不应触发单条 Embedding");
        }

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("精确直达路径不应触发批量 Embedding");
        }

        public double ComputeSimilarity(float[] embedding1, float[] embedding2)
        {
            return 1;
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

    // ── LLM 语义优先模式测试 ──────────────────────────────────────────────

    [Fact]
    public async Task BatchMatch_WhenLlmSemanticPriority_AndHardConflict_LlmEquivalentShouldAutoApply()
    {
        // 语义优先模式下，LLM 裁决 Equivalent 应覆盖确定性硬冲突规则 → AutoApply
        var source = new MatchSource
        {
            Project = "安装要求",
            Specification = "电压 ≥100V"
        };
        var candidate = new MatchCandidate
        {
            SpecId = 9901,
            Project = "安装要求",
            Specification = "电压 ≥220V",
            Embedding = [1f]
        };
        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 0.92,
            Reason = "客户环境100V可满足≥220V的上位规格"
        });
        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: equivalenceService);

        var result = await service.BatchMatchAsync(
            [source],
            [candidate],
            new MatchingConfig
            {
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 1,
                EnableDeterministicAutoApply = false,
                EnableLlmEquivalenceAdjudication = true,
                EnableLlmSemanticPriority = true
            });

        result.Results.Should().ContainSingle();
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply,
            "语义优先模式下 LLM Equivalent 应覆盖硬冲突规则");
        result.Results[0].LlmEquivalence?.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        equivalenceService.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task BatchMatch_WhenLlmSemanticPriority_LowEmbeddingCandidateShouldEnterLlm()
    {
        // 语义优先模式下，Embedding 分 0.55（低于默认 0.9 阈值）的候选应被召回并进入 LLM 裁决
        var source = new MatchSource
        {
            Project = "安装要求",
            Specification = "设备重量"
        };
        var candidate = new MatchCandidate
        {
            SpecId = 9902,
            Project = "安装要求",
            Specification = "设备质量",   // 语义相近但 Embedding 偏低
            Embedding = [1f]
        };
        var equivalenceService = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 0.88,
            Reason = "质量与重量在工程语境中等价"
        });
        // sourceEmbedding=[0.55f], candidateEmbedding=[1f] → 点积 = 0.55，低于默认 0.9 阈值
        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [0.55f], defaultCandidateEmbedding: [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: equivalenceService);

        // 标准模式：分数 0.55 < minScoreThreshold 0.9 → 候选被丢弃，LLM 不被调用
        var standardResult = await service.BatchMatchAsync(
            [source],
            [candidate],
            new MatchingConfig
            {
                MinScoreThreshold = 0.9,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.95,
                EnableDeterministicAutoApply = false,
                EnableLlmEquivalenceAdjudication = true,
                EnableLlmSemanticPriority = false
            });
        standardResult.Results[0].MatchedSpecId.Should().BeNull("标准模式下 0.55 分候选应被过滤");
        equivalenceService.Requests.Should().BeEmpty("标准模式下候选被过滤，LLM 不应被调用");

        // 语义优先模式：LlmSemanticRecallThreshold=0.5 → 0.55 >= 0.5，候选被保留，LLM 被调用
        var semanticResult = await service.BatchMatchAsync(
            [source],
            [candidate],
            new MatchingConfig
            {
                MinScoreThreshold = 0.9,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.95,
                EnableDeterministicAutoApply = false,
                EnableLlmEquivalenceAdjudication = true,
                EnableLlmSemanticPriority = true,
                LlmSemanticRecallThreshold = 0.5
            });
        semanticResult.Results[0].MatchedSpecId.Should().Be(9902,
            "语义优先模式下 0.55 分候选应被保留并命中");
        semanticResult.Results[0].Decision.Should().Be(MatchDecision.AutoApply,
            "LLM 裁决 Equivalent 应 AutoApply");
        equivalenceService.Requests.Should().ContainSingle("语义优先模式下 LLM 应被调用一次");
    }

    [Fact]
    public async Task BatchMatch_WhenCandidateAddsNegativePrefix_ShouldBlockAutoApply()
    {
        var source = new MatchSource
        {
            Project = "测试要求",
            Specification = "包含测试"
        };

        var candidate = new MatchCandidate
        {
            SpecId = 9903,
            Project = "测试要求",
            Specification = "非测试",
            Embedding = [1f]
        };

        var service = new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [source],
            [candidate],
            new MatchingConfig
            {
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.95
            });

        result.Results.Should().ContainSingle();
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Issues.Should().Contain(issue =>
            issue.Code == "negative_prefix_conflict" &&
            issue.Severity == "hard_conflict");
    }

    // ── 高 Embedding 语义自动通过测试 ──────────────────────────────
    private static SemanticKernelMatchingService BuildEmbAutoApplyService(
        MatchSource source, double srcEmb, LlmEquivalenceVerdict verdict)
    {
        var equ = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = verdict,
            ReasonType = verdict == LlmEquivalenceVerdict.Different
                ? LlmEquivalenceReasonType.SemanticDifference
                : LlmEquivalenceReasonType.Uncertain,
            Confidence = 0,
            Reason = "测试裁决"
        });
        return new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [(float)srcEmb], defaultCandidateEmbedding: [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: equ);
    }

    private static MatchingConfig EmbAutoApplyConfig(double threshold) => new()
    {
        MinScoreThreshold = 0,
        RecallTopK = 1,
        HighConfidenceThreshold = 0.95,
        AmbiguityMargin = 0.01,
        EnableDeterministicAutoApply = false,
        EnableLlmEquivalenceAdjudication = true,
        EnableLlmSemanticPriority = false,
        EmbeddingSemanticAutoApplyThreshold = threshold
    };

    [Fact]
    public async Task EmbAutoApply_WhenHighEmbeddingAndUncertain_ShouldAutoApply()
    {
        var source = new MatchSource { Project = "下料", Specification = "机械手臂运行不应产生碎屑" };
        var cand = new MatchCandidate { SpecId = 7001, Project = "下料", Specification = "机械手臂各机构不得摩擦产生磨屑", Embedding = [1f] };
        var service = BuildEmbAutoApplyService(source, 0.93, LlmEquivalenceVerdict.Uncertain);
        var r = await service.BatchMatchAsync([source], [cand], EmbAutoApplyConfig(0.90));
        r.Results[0].MatchedSpecId.Should().Be(7001);
        r.Results[0].Decision.Should().Be(MatchDecision.AutoApply, "高 Emb + 无冲突 + LLM uncertain + 阈值0.90 → 自动通过");
    }

    [Fact]
    public async Task EmbAutoApply_WhenThresholdZero_ShouldStayManual()
    {
        var source = new MatchSource { Project = "下料", Specification = "机械手臂运行不应产生碎屑" };
        var cand = new MatchCandidate { SpecId = 7001, Project = "下料", Specification = "机械手臂各机构不得摩擦产生磨屑", Embedding = [1f] };
        var service = BuildEmbAutoApplyService(source, 0.93, LlmEquivalenceVerdict.Uncertain);
        var r = await service.BatchMatchAsync([source], [cand], EmbAutoApplyConfig(0)); // 默认关闭
        r.Results[0].Decision.Should().Be(MatchDecision.ManualReview, "阈值0(默认)→ 行为不变,uncertain 仍转人工");
    }

    [Fact]
    public async Task EmbAutoApply_WhenHardNumericConflict_ShouldStayManual()
    {
        var source = new MatchSource { Project = "安装", Specification = "电压 ≥100V" };
        var cand = new MatchCandidate { SpecId = 7002, Project = "安装", Specification = "电压 ≥220V", Embedding = [1f] };
        var service = BuildEmbAutoApplyService(source, 0.93, LlmEquivalenceVerdict.Uncertain);
        var r = await service.BatchMatchAsync([source], [cand], EmbAutoApplyConfig(0.90));
        r.Results[0].Decision.Should().Be(MatchDecision.ManualReview, "硬冲突(数值)不被高 Emb 自动通过覆盖");
    }

    [Fact]
    public async Task EmbAutoApply_WhenLlmDifferent_ShouldStayManual()
    {
        var source = new MatchSource { Project = "下料", Specification = "机械手臂运行不应产生碎屑" };
        var cand = new MatchCandidate { SpecId = 7003, Project = "下料", Specification = "机械手臂各机构不得摩擦产生磨屑", Embedding = [1f] };
        var service = BuildEmbAutoApplyService(source, 0.93, LlmEquivalenceVerdict.Different);
        var r = await service.BatchMatchAsync([source], [cand], EmbAutoApplyConfig(0.90));
        r.Results[0].Decision.Should().Be(MatchDecision.ManualReview, "LLM 明确判 Different 不被覆盖");
    }

    [Fact]
    public async Task EmbAutoApply_WhenEmbeddingBelowThreshold_ShouldStayManual()
    {
        var source = new MatchSource { Project = "下料", Specification = "机械手臂运行不应产生碎屑" };
        var cand = new MatchCandidate { SpecId = 7004, Project = "下料", Specification = "机械手臂各机构不得摩擦产生磨屑", Embedding = [1f] };
        var service = BuildEmbAutoApplyService(source, 0.80, LlmEquivalenceVerdict.Uncertain);
        var r = await service.BatchMatchAsync([source], [cand], EmbAutoApplyConfig(0.90));
        r.Results[0].Decision.Should().Be(MatchDecision.ManualReview, "Emb 0.80 < 阈值0.90 → 不自动通过");
    }

    [Fact]
    public async Task NumericConflict_WhenCandidateHasBilingualDuplicateOfSameValue_ShouldNotFlagConflict()
    {
        // 回归：候选为中英对照（同一数值出现两份，如"30天 / 30 day"），
        // 源仅含中文一份。折叠重复值前会被判"数量 1 vs 2 → numeric_unit_conflict 硬冲突"，
        // 这是误判。折叠后两侧均为 1 份且数值相等，不应产生数值冲突。
        var source = new MatchSource
        {
            Project = "机构设计配接",
            Specification = "封闭式设备内部须设有摄像头监控，录像可保存30天以上"
        };
        var cand = new MatchCandidate
        {
            SpecId = 8262,
            Project = "机构设计配接",
            Specification = "8.封闭式设备内部带有摄像头监控，且可保存30天以上\n"
                + "8.The enclosed device has a camera monitoring inside, and it can be saved for more than 30 day",
            Embedding = [1f]
        };

        var service = BuildEmbAutoApplyService(source, 0.93, LlmEquivalenceVerdict.Uncertain);
        var r = await service.BatchMatchAsync([source], [cand], EmbAutoApplyConfig(0.90));

        r.Results[0].Issues.Should().NotContain(
            issue => issue.Code == "numeric_unit_conflict",
            "同一数值的中英两份表达应被折叠，不应误判为数值数量不等");
    }

    [Fact]
    public async Task NumericConflict_WhenValuesGenuinelyDiffer_ShouldStillFlagConflict()
    {
        // 反向保护：折叠重复不能掩盖真实的数值差异。
        // 源"保存30天" vs 候选"保存60天" 仍须产出 numeric_unit_conflict。
        var source = new MatchSource
        {
            Project = "机构设计配接",
            Specification = "录像可保存30天以上"
        };
        var cand = new MatchCandidate
        {
            SpecId = 8263,
            Project = "机构设计配接",
            Specification = "录像可保存60天以上",
            Embedding = [1f]
        };

        var service = BuildEmbAutoApplyService(source, 0.93, LlmEquivalenceVerdict.Uncertain);
        var r = await service.BatchMatchAsync([source], [cand], EmbAutoApplyConfig(0.90));

        r.Results[0].Issues.Should().Contain(
            issue => issue.Code == "numeric_unit_conflict",
            "30天 与 60天 是真实数值差异，折叠重复后仍须检出冲突");
    }
}
