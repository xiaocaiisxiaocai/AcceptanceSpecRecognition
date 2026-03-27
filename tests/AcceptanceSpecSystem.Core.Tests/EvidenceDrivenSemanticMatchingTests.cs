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

    private sealed class FixedSourceEmbeddingService : IEmbeddingService
    {
        private readonly string _sourceText;
        private readonly float[] _sourceEmbedding;

        public FixedSourceEmbeddingService(string sourceText, float[] sourceEmbedding)
        {
            _sourceText = sourceText;
            _sourceEmbedding = sourceEmbedding;
        }

        public bool IsAvailable => true;

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(text == _sourceText ? _sourceEmbedding : [0f]);
        }

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            var list = texts.Select(text => text == _sourceText ? _sourceEmbedding : [0f]).ToList();
            return Task.FromResult(list);
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
}
