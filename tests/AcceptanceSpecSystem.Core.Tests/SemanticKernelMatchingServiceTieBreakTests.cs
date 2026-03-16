using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Core.Tests;

public class SemanticKernelMatchingServiceTieBreakTests
{
    [Fact]
    public async Task BatchMatch_WhenScoreTie_ShouldPreferCandidateWithAcceptance()
    {
        var service = new SemanticKernelMatchingService(new ConstantEmbeddingService(), NullLogger<SemanticKernelMatchingService>.Instance);

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 2142,
                Project = "设备安装需求",
                Specification = "设备供应商在到厂前提供设备的空压位置大小及流量",
                Acceptance = null,
                Remark = null
            },
            new()
            {
                SpecId = 2348,
                Project = "设备安装需求",
                Specification = "设备供应商在到厂前提供设备的空压位置大小及流量",
                Acceptance = "OK",
                Remark = null
            }
        };

        var result = await service.BatchMatchAsync(
            new[]
            {
                new MatchSource
                {
                    Project = "设备安装需求",
                    Specification = "设备供应商在到厂前提供设备的空压位置大小及流量"
                }
            },
            candidates,
            new MatchingConfig { MinScoreThreshold = 0.0 });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(2348);
        result.Results[0].MatchedAcceptance.Should().Be("OK");
    }

    [Fact]
    public async Task BatchMatch_WhenScoreTieAndAcceptanceSame_ShouldPreferHigherSpecId()
    {
        var service = new SemanticKernelMatchingService(new ConstantEmbeddingService(), NullLogger<SemanticKernelMatchingService>.Instance);

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 100,
                Project = "项目A",
                Specification = "规格A",
                Acceptance = null,
                Remark = null
            },
            new()
            {
                SpecId = 101,
                Project = "项目A",
                Specification = "规格A",
                Acceptance = null,
                Remark = null
            }
        };

        var result = await service.BatchMatchAsync(
            new[]
            {
                new MatchSource
                {
                    Project = "项目A",
                    Specification = "规格A"
                }
            },
            candidates,
            new MatchingConfig { MinScoreThreshold = 0.0 });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(101);
    }

    [Fact]
    public async Task BatchMatch_MultiStage_ShouldRerankToBusinessSaferCandidate()
    {
        var source = new MatchSource
        {
            Project = "收板模式",
            Specification = "速度 100 mm/s"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 1,
                Project = "收板模式",
                Specification = "速度 100 mm/s",
                Acceptance = "SAFE",
                Embedding = new[] { 0.90f, 0.10f }
            },
            new()
            {
                SpecId = 2,
                Project = "投板模式",
                Specification = "速度 100 mm/s",
                Acceptance = "RISKY",
                Embedding = new[] { 0.95f, 0.05f }
            }
        };

        var service = new SemanticKernelMatchingService(
            new SourceOnlyEmbeddingService(source.CombinedText, new[] { 1f, 0f }),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var singleStage = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.SingleStage,
                MinScoreThreshold = 0.0
            });

        var multiStage = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 2,
                AmbiguityMargin = 0.01
            });

        singleStage.Results.Should().HaveCount(1);
        singleStage.Results[0].MatchedSpecId.Should().Be(2);

        multiStage.Results.Should().HaveCount(1);
        multiStage.Results[0].MatchedSpecId.Should().Be(1);
        multiStage.Results[0].MatchingStrategy.Should().Be(MatchingStrategy.MultiStage);
        multiStage.Results[0].RecalledCandidateCount.Should().Be(2);
        multiStage.Results[0].IsAmbiguous.Should().BeFalse();
        multiStage.Results[0].RerankSummary.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task BatchMatch_MultiStage_WhenTop1AndTop2GapWithinMargin_ShouldMarkAmbiguous()
    {
        var service = new SemanticKernelMatchingService(new ConstantEmbeddingService(), NullLogger<SemanticKernelMatchingService>.Instance);
        var source = new MatchSource
        {
            Project = "项目A",
            Specification = "规格A"
        };

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 10,
                Project = "项目A",
                Specification = "规格A",
                Acceptance = "OK"
            },
            new()
            {
                SpecId = 11,
                Project = "项目A",
                Specification = "规格A",
                Acceptance = null
            }
        };

        var result = await service.BatchMatchAsync(
            [source],
            candidates,
            new MatchingConfig
            {
                MatchingStrategy = MatchingStrategy.MultiStage,
                MinScoreThreshold = 0.0,
                RecallTopK = 2,
                AmbiguityMargin = 0.05
            });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(10);
        result.Results[0].IsAmbiguous.Should().BeTrue();
        result.Results[0].ScoreGap.Should().Be(0);
    }

    private sealed class ConstantEmbeddingService : IEmbeddingService
    {
        public bool IsAvailable => true;

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new[] { 1f, 1f, 1f });
        }

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            var list = texts.Select(_ => new[] { 1f, 1f, 1f }).ToList();
            return Task.FromResult(list);
        }

        public double ComputeSimilarity(float[] embedding1, float[] embedding2)
        {
            return 1.0;
        }
    }

    private sealed class SourceOnlyEmbeddingService : IEmbeddingService
    {
        private readonly string _targetText;
        private readonly float[] _embedding;

        public SourceOnlyEmbeddingService(string targetText, float[] embedding)
        {
            _targetText = targetText;
            _embedding = embedding;
        }

        public bool IsAvailable => true;

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(text == _targetText ? _embedding : new[] { 0f, 0f });
        }

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            var list = texts.Select(text => text == _targetText ? _embedding : new[] { 0f, 0f }).ToList();
            return Task.FromResult(list);
        }

        public double ComputeSimilarity(float[] embedding1, float[] embedding2)
        {
            if (embedding1.Length == 0 || embedding2.Length == 0)
                return 0;

            return embedding1.Zip(embedding2, (left, right) => left * right).Sum();
        }
    }
}
