using System.Collections.Concurrent;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Core.Tests;

public class SemanticKernelMatchingBatchParallelismTests
{
    [Fact]
    public async Task BatchMatchAsync_ShouldProcessRowsConcurrently_WhenLlmParallelismGreaterThanOne()
    {
        var sources = Enumerable.Range(1, 4)
            .Select(index => new MatchSource
            {
                Project = $"项目{index}",
                Specification = $"规格{index}-源"
            })
            .ToList();

        var candidates = Enumerable.Range(1, 4)
            .Select(index => new MatchCandidate
            {
                SpecId = index,
                Project = $"项目{index}",
                Specification = $"规格{index}-候选",
                Acceptance = $"验收{index}",
                Embedding = [1f]
            })
            .ToList();

        var equivalenceService = new DelayedEquivalentLlmEquivalenceService(TimeSpan.FromMilliseconds(120));
        var service = new SemanticKernelMatchingService(
            new StableEmbeddingService(),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: equivalenceService);

        var result = await service.BatchMatchAsync(
            sources,
            candidates,
            new MatchingConfig
            {
                MinScoreThreshold = 0,
                RecallTopK = 1,
                LlmParallelism = 4
            });

        result.Results.Should().HaveCount(4);
        result.Results.Select(item => item.MatchedSpecId).Should().ContainInOrder(1, 2, 3, 4);
        equivalenceService.MaxConcurrency.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task BatchMatchAsync_ShouldBypassEmbeddingAndLlm_WhenSourceAndCandidateTextAreExactlyEqual()
    {
        var sources = Enumerable.Range(1, 3)
            .Select(index => new MatchSource
            {
                Project = $"项目{index}",
                Specification = $"规格{index}"
            })
            .ToList();

        var candidates = sources
            .Select((source, index) => new MatchCandidate
            {
                SpecId = index + 10,
                Project = source.Project,
                Specification = source.Specification,
                Acceptance = $"验收{index + 1}",
                Embedding = [1f]
            })
            .ToList();

        var embeddingService = new ThrowingEmbeddingService();
        var equivalenceService = new ThrowingLlmEquivalenceService();
        var service = new SemanticKernelMatchingService(
            embeddingService,
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: equivalenceService);

        var result = await service.BatchMatchAsync(
            sources,
            candidates,
            new MatchingConfig
            {
                RecallTopK = 1,
                LlmParallelism = 3
            });

        result.Results.Should().HaveCount(3);
        result.Results.Select(item => item.MatchedSpecId).Should().ContainInOrder(10, 11, 12);
        result.Results.Should().OnlyContain(item => item.Decision == MatchDecision.AutoApply);
        result.Results.Should().OnlyContain(item =>
            item.LlmEquivalence != null &&
            item.LlmEquivalence.Verdict == LlmEquivalenceVerdict.Equivalent);
        embeddingService.GenerateEmbeddingsCallCount.Should().Be(0);
        equivalenceService.CallCount.Should().Be(0);
    }

    private sealed class StableEmbeddingService : IEmbeddingService
    {
        public bool IsAvailable => true;

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new[] { 1f });
        }

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(texts.Select(_ => new[] { 1f }).ToList());
        }

        public double ComputeSimilarity(float[] embedding1, float[] embedding2)
        {
            return 1;
        }
    }

    private sealed class ThrowingEmbeddingService : IEmbeddingService
    {
        public bool IsAvailable => true;

        public int GenerateEmbeddingsCallCount { get; private set; }

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("精确匹配不应触发单条 Embedding");
        }

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            GenerateEmbeddingsCallCount++;
            throw new InvalidOperationException("精确匹配不应触发批量 Embedding");
        }

        public double ComputeSimilarity(float[] embedding1, float[] embedding2)
        {
            return 1;
        }
    }

    private sealed class DelayedEquivalentLlmEquivalenceService : ILlmEquivalenceAdjudicationService
    {
        private readonly TimeSpan _delay;
        private int _currentConcurrency;

        public DelayedEquivalentLlmEquivalenceService(TimeSpan delay)
        {
            _delay = delay;
        }

        public ConcurrentQueue<string> RequestOrder { get; } = [];

        public int MaxConcurrency { get; private set; }

        public async Task<LlmEquivalenceAdjudicationResult?> AdjudicateAsync(
            LlmEquivalenceAdjudicationRequest request,
            CancellationToken cancellationToken = default)
        {
            var current = Interlocked.Increment(ref _currentConcurrency);
            MaxConcurrency = Math.Max(MaxConcurrency, current);

            try
            {
                RequestOrder.Enqueue(request.SourceSpecification);
                await Task.Delay(_delay, cancellationToken);
                return new LlmEquivalenceAdjudicationResult
                {
                    Verdict = LlmEquivalenceVerdict.Equivalent,
                    ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
                    Confidence = 0.99,
                    Reason = "测试替身：可直接视为等价"
                };
            }
            finally
            {
                Interlocked.Decrement(ref _currentConcurrency);
            }
        }

        public bool TryParseAdjudicationResult(string raw, out LlmEquivalenceAdjudicationResult result)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ThrowingLlmEquivalenceService : ILlmEquivalenceAdjudicationService
    {
        public int CallCount { get; private set; }

        public Task<LlmEquivalenceAdjudicationResult?> AdjudicateAsync(
            LlmEquivalenceAdjudicationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("精确匹配不应触发 LLM 等价裁决");
        }

        public bool TryParseAdjudicationResult(string raw, out LlmEquivalenceAdjudicationResult result)
        {
            throw new NotSupportedException();
        }
    }
}
