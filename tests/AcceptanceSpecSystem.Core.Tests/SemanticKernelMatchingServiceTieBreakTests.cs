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
            new[] { "设备安装需求 设备供应商在到厂前提供设备的空压位置大小及流量" },
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
            new[] { "项目A 规格A" },
            candidates,
            new MatchingConfig { MinScoreThreshold = 0.0 });

        result.Results.Should().HaveCount(1);
        result.Results[0].MatchedSpecId.Should().Be(101);
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
}

