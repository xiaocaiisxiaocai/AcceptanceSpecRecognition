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
                LlmParallelism = 4,
                EnableLlmEquivalenceAdjudication = true
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

    [Fact]
    public async Task BatchMatchAsync_ShouldCountCanonicalShortcutRowsInProgress()
    {
        var sources = new List<MatchSource>
        {
            new()
            {
                Project = "功率要求",
                Specification = "功率等于7.5kW"
            },
            new()
            {
                Project = "安装要求",
                Specification = "设备位置"
            }
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 21,
                Project = "功率要求",
                Specification = "功率等于7500W",
                Embedding = [1f]
            },
            new()
            {
                SpecId = 22,
                Project = "安装要求",
                Specification = "设备安装位置",
                Embedding = [1f]
            }
        };

        var progress = new RecordingProgress();
        var service = new SemanticKernelMatchingService(
            new StableEmbeddingService(),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            sources,
            candidates,
            new MatchingConfig
            {
                MinScoreThreshold = 0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.5
            },
            progress);

        result.Results.Should().HaveCount(2);
        result.Results.Select(item => item.MatchedSpecId).Should().ContainInOrder(21, 22);
        progress.Events.Should().Contain(item =>
            item.Stage == "matching" &&
            item.CompletedItems == 1 &&
            item.TotalItems == 2);
        progress.Events.Last(item => item.Stage == "matching").CompletedItems.Should().Be(2);
    }

    [Fact]
    public async Task BatchMatchAsync_ShouldShareLlmBudgetBetweenRerankAndEquivalence()
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
                SpecId = 31,
                Project = "安装要求",
                Specification = "设备建议安装在底座附近",
                Acceptance = "候选一",
                Embedding = [0.99f]
            },
            new()
            {
                SpecId = 32,
                Project = "安装要求",
                Specification = "设备应稳固安装于底座",
                Acceptance = "候选二",
                Embedding = [0.98f]
            }
        };

        var rerankService = new CountingRerankService(new LlmCandidateRerankResult
        {
            SelectedSpecId = 32,
            Confidence = 0.95,
            Reason = "候选二语义更贴近"
        });
        var equivalenceService = new CountingEquivalenceService();
        var service = new SemanticKernelMatchingService(
            new StableEmbeddingService(),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmCandidateRerankService: rerankService,
            llmEquivalenceAdjudicationService: equivalenceService);

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MinScoreThreshold = 0,
                RecallTopK = 2,
                HighConfidenceThreshold = 1,
                AmbiguityMargin = 0,
                LlmParallelism = 1,
                EnableDeterministicAutoApply = false,
                EnableLlmEquivalenceAdjudication = true,
                LlmMaxCallsPerBatch = 1
            });

        result.Results.Should().ContainSingle();
        result.Results[0].MatchedSpecId.Should().Be(32);
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].SelectionSummary.Should().Contain("LLM 调用已达批次上限");
        rerankService.CallCount.Should().Be(1);
        equivalenceService.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task BatchMatchAsync_ShouldCountRetriesAgainstLlmBudget()
    {
        var source = new MatchSource
        {
            Project = "安装要求",
            Specification = "设备需要固定到底座"
        };
        var candidate = new MatchCandidate
        {
            SpecId = 33,
            Project = "安装要求",
            Specification = "设备应固定于底座附近",
            Embedding = [1f]
        };
        var equivalenceService = new AlwaysFailingLlmEquivalenceService();
        var service = new SemanticKernelMatchingService(
            new StableEmbeddingService(),
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
                LlmParallelism = 1,
                LlmRetryCount = 1,
                EnableDeterministicAutoApply = false,
                EnableLlmEquivalenceAdjudication = true,
                LlmMaxCallsPerBatch = 1
            });

        result.Results.Should().ContainSingle();
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].SelectionSummary.Should().Contain("LLM 调用已达批次上限");
        equivalenceService.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task BatchMatchAsync_ShouldRetryTimedOutLlmEquivalenceBeforeFallingBack()
    {
        var source = new MatchSource
        {
            Project = "安装要求",
            Specification = "设备需要固定到底座"
        };
        var candidate = new MatchCandidate
        {
            SpecId = 41,
            Project = "安装要求",
            Specification = "设备应固定于底座附近",
            Embedding = [1f]
        };
        var equivalenceService = new TimeoutThenEquivalentLlmEquivalenceService();
        var service = new SemanticKernelMatchingService(
            new StableEmbeddingService(),
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
                LlmParallelism = 1,
                LlmRowTimeoutSeconds = 1,
                LlmRetryCount = 1,
                EnableDeterministicAutoApply = false,
                EnableLlmEquivalenceAdjudication = true,
                LlmMaxCallsPerBatch = 5
            });

        result.Results.Should().ContainSingle();
        result.Results[0].Decision.Should().Be(MatchDecision.AutoApply);
        result.Results[0].LlmEquivalence?.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        equivalenceService.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task BatchMatchAsync_ShouldCountLlmRetryAttemptsAgainstBatchBudget()
    {
        var source = new MatchSource
        {
            Project = "安装要求",
            Specification = "设备需要固定到底座"
        };
        var candidate = new MatchCandidate
        {
            SpecId = 42,
            Project = "安装要求",
            Specification = "设备应固定于底座附近",
            Embedding = [1f]
        };
        var equivalenceService = new TimeoutThenEquivalentLlmEquivalenceService();
        var service = new SemanticKernelMatchingService(
            new StableEmbeddingService(),
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
                LlmParallelism = 1,
                LlmRowTimeoutSeconds = 1,
                LlmRetryCount = 1,
                EnableDeterministicAutoApply = false,
                EnableLlmEquivalenceAdjudication = true,
                LlmMaxCallsPerBatch = 1
            });

        result.Results.Should().ContainSingle();
        result.Results[0].Decision.Should().Be(MatchDecision.ManualReview);
        result.Results[0].SelectionSummary.Should().Contain("LLM 调用已达批次上限");
        equivalenceService.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task BatchMatchAsync_ShouldOpenCircuitAfterRepeatedLlmFailures()
    {
        var sources = Enumerable.Range(1, 5)
            .Select(index => new MatchSource
            {
                Project = $"项目{index}",
                Specification = $"源规格{index}"
            })
            .ToList();
        var candidates = sources
            .Select((source, index) => new MatchCandidate
            {
                SpecId = index + 50,
                Project = source.Project,
                Specification = $"候选规格{index + 1}",
                Embedding = [1f]
            })
            .ToList();
        var equivalenceService = new AlwaysFailingLlmEquivalenceService();
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
                HighConfidenceThreshold = 1,
                LlmParallelism = 1,
                LlmRetryCount = 0,
                LlmCircuitBreakFailures = 3,
                EnableDeterministicAutoApply = false,
                EnableLlmEquivalenceAdjudication = true,
                LlmMaxCallsPerBatch = 10
            });

        result.Results.Should().HaveCount(5);
        result.Results.Should().OnlyContain(item => item.Decision == MatchDecision.ManualReview);
        equivalenceService.CallCount.Should().Be(3);
        result.Results.Skip(3).Should().OnlyContain(item =>
            item.SelectionSummary != null &&
            item.SelectionSummary.Contains("LLM 失败率过高，已触发熔断"));
    }

    private sealed class RecordingProgress : IProgress<BatchMatchProgress>
    {
        public List<BatchMatchProgress> Events { get; } = [];

        public void Report(BatchMatchProgress value)
        {
            Events.Add(value);
        }
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

    private sealed class CountingRerankService : ILlmCandidateRerankService
    {
        private readonly LlmCandidateRerankResult? _result;

        public CountingRerankService(LlmCandidateRerankResult? result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<LlmCandidateRerankResult?> RerankAsync(
            LlmCandidateRerankRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_result);
        }

        public bool TryParseRerankResult(string raw, out LlmCandidateRerankResult result)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CountingEquivalenceService : ILlmEquivalenceAdjudicationService
    {
        public int CallCount { get; private set; }

        public Task<LlmEquivalenceAdjudicationResult?> AdjudicateAsync(
            LlmEquivalenceAdjudicationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<LlmEquivalenceAdjudicationResult?>(new LlmEquivalenceAdjudicationResult
            {
                Verdict = LlmEquivalenceVerdict.Equivalent,
                ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
                Confidence = 0.96,
                Reason = "测试替身：等价"
            });
        }

        public bool TryParseAdjudicationResult(string raw, out LlmEquivalenceAdjudicationResult result)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TimeoutThenEquivalentLlmEquivalenceService : ILlmEquivalenceAdjudicationService
    {
        public int CallCount { get; private set; }

        public async Task<LlmEquivalenceAdjudicationResult?> AdjudicateAsync(
            LlmEquivalenceAdjudicationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount == 1)
            {
                await Task.Yield();
                throw new OperationCanceledException(cancellationToken);
            }

            return new LlmEquivalenceAdjudicationResult
            {
                Verdict = LlmEquivalenceVerdict.Equivalent,
                ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
                Confidence = 0.97,
                Reason = "重试后判定等价"
            };
        }

        public bool TryParseAdjudicationResult(string raw, out LlmEquivalenceAdjudicationResult result)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class AlwaysFailingLlmEquivalenceService : ILlmEquivalenceAdjudicationService
    {
        public int CallCount { get; private set; }

        public Task<LlmEquivalenceAdjudicationResult?> AdjudicateAsync(
            LlmEquivalenceAdjudicationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("测试替身：LLM 持续失败");
        }

        public bool TryParseAdjudicationResult(string raw, out LlmEquivalenceAdjudicationResult result)
        {
            throw new NotSupportedException();
        }
    }
}
