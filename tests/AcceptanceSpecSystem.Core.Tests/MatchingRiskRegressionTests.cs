using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Core.Tests;

/// <summary>
/// 智能填充误匹配风险回归测试。
/// 逐条覆盖 N1（大小写合并单位前缀）、N2（语义优先覆盖硬冲突）、N3（fail-open）、
/// 风险4（近似层裸数字）、风险5（项目子串包含）、风险6（近似层歧义）的修复。
/// </summary>
public class MatchingRiskRegressionTests
{
    // ── N2 + 风险2：语义优先模式下，量纲型硬冲突（数值/单位）不可被 LLM Equivalent 覆盖 ──

    [Fact]
    public async Task BatchMatch_SemanticPriority_WithNumericHardConflict_ShouldManualReview()
    {
        var service = CreateService(
            embeddingSimilarity: 0.98,
            llmVerdict: LlmEquivalenceVerdict.Equivalent,
            llmConfidence: 1.0);

        var result = await service.BatchMatchAsync(
            [new MatchSource { Project = "电机", Specification = "转速 3000 rpm" }],
            [new MatchCandidate { SpecId = 1, Project = "电机", Specification = "转速 300 rpm", Acceptance = "OK" }],
            new MatchingConfig
            {
                MinScoreThreshold = 0.0,
                EnableLlmSemanticPriority = true,
                LlmSemanticRecallThreshold = 0.5
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Issues.Should().Contain(issue => issue.Code == "numeric_unit_conflict");
    }

    // ── 语义优先模式保留价值：方向/极性类硬冲突仍可交由 LLM 覆盖（不应被量纲门禁误伤） ──

    [Fact]
    public async Task BatchMatch_SemanticPriority_WithPolarityHardConflictAndLlmEquivalent_ShouldAutoApply()
    {
        var service = CreateService(
            embeddingSimilarity: 0.98,
            llmVerdict: LlmEquivalenceVerdict.Equivalent,
            llmConfidence: 1.0);

        var result = await service.BatchMatchAsync(
            [new MatchSource { Project = "电机", Specification = "转向 正转" }],
            [new MatchCandidate { SpecId = 1, Project = "电机", Specification = "转向 正转", Acceptance = "OK" }],
            new MatchingConfig
            {
                MinScoreThreshold = 0.0,
                EnableLlmSemanticPriority = true,
                LlmSemanticRecallThreshold = 0.5
            });

        // 极性冲突属于语义方向类，语义优先下 LLM 判等价应放行（保留语义优先的初衷）。
        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
    }

    // ── N1：精确匹配键大小写不敏感，mΩ 与 MΩ 不再被当作精确一致直接放行 ──

    [Fact]
    public async Task BatchMatch_MilliOhmVsMegaOhm_ShouldNotAutoApply()
    {
        var service = CreateService(
            embeddingSimilarity: 1.0,
            llmVerdict: LlmEquivalenceVerdict.Equivalent,
            llmConfidence: 1.0);

        var result = await service.BatchMatchAsync(
            [new MatchSource { Project = "电阻", Specification = "阻值 10mΩ" }],
            [new MatchCandidate { SpecId = 1, Project = "电阻", Specification = "阻值 10MΩ", Acceptance = "OK" }],
            new MatchingConfig { MinScoreThreshold = 0.0 });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].Issues.Should().Contain(issue => issue.Code == "numeric_unit_conflict");
    }

    // ── N3 + 风险1：LLM 失败（异常）回退时，高 Embedding 自动通过路径不再 fail-open ──

    [Fact]
    public async Task BatchMatch_LlmFailure_WithHighEmbedding_ShouldManualReview()
    {
        var service = new SemanticKernelMatchingService(
            new FixedSimilarityEmbeddingService(0.98),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: new ThrowingLlmAdjudicationService());

        var result = await service.BatchMatchAsync(
            [new MatchSource { Project = "设备", Specification = "需具备过载保护" }],
            [new MatchCandidate { SpecId = 1, Project = "设备", Specification = "应具备过载保护功能", Acceptance = "OK" }],
            new MatchingConfig
            {
                MinScoreThreshold = 0.0,
                EmbeddingSemanticAutoApplyThreshold = 0.9
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
    }

    // ── 风险4：近似层裸数字差异不再被骨架抹平后确定性放行 ──

    [Fact]
    public async Task BatchMatch_BareNumberDifference_ShouldNotDeterministicAutoApply()
    {
        var service = CreateService(embeddingSimilarity: 0.5);

        var result = await service.BatchMatchAsync(
            [new MatchSource { Project = "电源", Specification = "额定电压 220V，长度 300" }],
            [new MatchCandidate { SpecId = 1, Project = "电源", Specification = "额定电压 220V，长度 3000", Acceptance = "OK" }],
            new MatchingConfig { MinScoreThreshold = 0.0 });

        result.Results.Should().HaveCount(1);
        result.Results[0].SelectionMode.Should().NotBe(MatchSelectionMode.ExactShortcut);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
    }

    // ── 风险6：近似层仅规格模式下多条候选判歧义，不再静默取首条 ──

    [Fact]
    public async Task BatchMatch_ApproximateCanonical_SpecificationOnlyMultipleCandidates_ShouldBeAmbiguous()
    {
        var service = CreateService(embeddingSimilarity: 0.5);

        var result = await service.BatchMatchAsync(
            [new MatchSource { Project = "项目", Specification = "外径 10mm" }],
            [
                new MatchCandidate { SpecId = 1, Project = "项目A", Specification = "外径 10.005mm", Acceptance = "OK" },
                new MatchCandidate { SpecId = 2, Project = "项目B", Specification = "外径 10.004mm", Acceptance = "NG需返工" }
            ],
            new MatchingConfig
            {
                MinScoreThreshold = 0.0,
                MatchingMode = MatchingMode.SpecificationOnly
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].IsAmbiguous.Should().BeTrue();
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
    }

    // ── 风险5：项目短词被长词"包含"时不再给 0.85 接近分 ──

    [Fact]
    public async Task BatchMatch_ProjectContainment_ShortPrefix_ShouldNotGetContainmentScore()
    {
        var service = CreateService(embeddingSimilarity: 0.8);

        var result = await service.BatchMatchAsync(
            [new MatchSource { Project = "A机", Specification = "外观检查" }],
            [new MatchCandidate { SpecId = 1, Project = "A机型B", Specification = "外观检查", Acceptance = "OK" }],
            new MatchingConfig { MinScoreThreshold = 0.0 });

        result.Results.Should().HaveCount(1);
        result.Results[0].ScoreDetails["ProjectMatch"].Should().Be(0.5);
    }

    // ── N1 底层能力：mΩ 可归一到电阻量纲 ──

    [Fact]
    public void SpecCanonicalizer_MilliOhm_ShouldNormalizeToResistance()
    {
        var canonicalizer = new SpecCanonicalizer();

        canonicalizer.TryNormalizeToBaseUnit(10, "mΩ", out var baseValue, out var dimension)
            .Should().BeTrue();
        baseValue.Should().BeApproximately(0.01, 1e-12);
        dimension.Should().Be("resistance");
    }

    // ── 风险4 底层能力：裸数字提取排除带单位数字 ──

    [Fact]
    public void SpecCanonicalizer_ExtractBareNumbers_ShouldExcludeUnitCoveredNumbers()
    {
        var canonicalizer = new SpecCanonicalizer();

        canonicalizer.ExtractBareNumbers("额定电压 220V，长度 300")
            .Should().BeEquivalentTo(new[] { 300.0 });
    }

    // ── 测试辅助 ──────────────────────────────────────────────

    private static SemanticKernelMatchingService CreateService(
        double embeddingSimilarity,
        LlmEquivalenceVerdict? llmVerdict = null,
        double llmConfidence = 0)
    {
        ILlmEquivalenceAdjudicationService? llm = llmVerdict.HasValue
            ? new FakeLlmAdjudicationService(llmVerdict.Value, llmConfidence)
            : null;

        return new SemanticKernelMatchingService(
            new FixedSimilarityEmbeddingService(embeddingSimilarity),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: llm);
    }

    private sealed class FixedSimilarityEmbeddingService : IEmbeddingService
    {
        private readonly double _similarity;

        public FixedSimilarityEmbeddingService(double similarity) => _similarity = similarity;

        public bool IsAvailable => true;

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new[] { 1f });

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(texts.Select(_ => new[] { 1f }).ToList());

        public double ComputeSimilarity(float[] embedding1, float[] embedding2) => _similarity;
    }

    private sealed class FakeLlmAdjudicationService : ILlmEquivalenceAdjudicationService
    {
        private readonly LlmEquivalenceVerdict _verdict;
        private readonly double _confidence;

        public FakeLlmAdjudicationService(LlmEquivalenceVerdict verdict, double confidence)
        {
            _verdict = verdict;
            _confidence = confidence;
        }

        public Task<LlmEquivalenceAdjudicationResult?> AdjudicateAsync(
            LlmEquivalenceAdjudicationRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<LlmEquivalenceAdjudicationResult?>(new LlmEquivalenceAdjudicationResult
            {
                Verdict = _verdict,
                ReasonType = _verdict == LlmEquivalenceVerdict.Equivalent
                    ? LlmEquivalenceReasonType.EquivalentExpression
                    : LlmEquivalenceReasonType.Uncertain,
                Confidence = _confidence,
                Reason = "测试桩"
            });
        }

        public bool TryParseAdjudicationResult(string raw, out LlmEquivalenceAdjudicationResult result)
        {
            result = null!;
            return false;
        }
    }

    private sealed class ThrowingLlmAdjudicationService : ILlmEquivalenceAdjudicationService
    {
        public Task<LlmEquivalenceAdjudicationResult?> AdjudicateAsync(
            LlmEquivalenceAdjudicationRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("模拟 LLM 服务故障");

        public bool TryParseAdjudicationResult(string raw, out LlmEquivalenceAdjudicationResult result)
        {
            result = null!;
            return false;
        }
    }
}
