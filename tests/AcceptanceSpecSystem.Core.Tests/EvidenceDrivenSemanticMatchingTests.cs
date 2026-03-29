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
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                AmbiguityMargin = 0.01
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(88);
        result.Results[0].Decision.Should().Be(MatchDecision.Reject);
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
                new MatchSource { Project = "项目A", Specification = "规格A" },
                new MatchSource { Project = "项目B", Specification = "规格B" }
            ],
            [
                new MatchCandidate { SpecId = 1, Project = "项目A", Specification = "规格A" }
            ],
            new MatchingConfig { MinScoreThreshold = 0.0 });

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
