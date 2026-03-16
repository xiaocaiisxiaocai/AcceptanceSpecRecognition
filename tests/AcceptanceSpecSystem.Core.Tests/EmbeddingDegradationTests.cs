using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Core.Tests;

/// <summary>
/// Embedding 失败行为测试：Embedding 不可用时应直接抛出异常
/// </summary>
public class EmbeddingDegradationTests
{
    [Fact]
    public async Task FindMatches_WhenEmbeddingFails_ShouldThrowUnavailableException()
    {
        // 使用会抛异常的 Embedding 服务
        var failingEmbedding = new FailingEmbeddingService();
        var logger = NullLogger<SemanticKernelMatchingService>.Instance;

        var service = new SemanticKernelMatchingService(failingEmbedding, logger);

        var candidates = new List<MatchCandidate>
        {
            new()
            {
                SpecId = 1,
                Project = "项目A",
                Specification = "规格A",
                Acceptance = "OK"
            }
        };

        var config = new MatchingConfig { MinScoreThreshold = 0.0 };
        var act = async () => await service.FindMatchesAsync(
            new MatchSource
            {
                Project = "项目A",
                Specification = "规格A"
            },
            candidates,
            config);

        // 不再降级，直接抛出 Embedding 不可用异常
        await act.Should().ThrowAsync<AiServiceUnavailableException>()
            .WithMessage("*Embedding*不可用*");
    }

    /// <summary>
    /// 模拟 Embedding 不可用的服务
    /// </summary>
    private class FailingEmbeddingService : IEmbeddingService
    {
        public bool IsAvailable => false;

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            throw new AiServiceUnavailableException("Embedding 测试模拟不可用");
        }

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            throw new AiServiceUnavailableException("Embedding 测试模拟不可用");
        }

        public double ComputeSimilarity(float[] embedding1, float[] embedding2)
        {
            return 0.0;
        }
    }
}
