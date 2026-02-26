using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Core.Tests;

/// <summary>
/// Embedding 降级逻辑测试：Embedding 不可用时应降级到 Levenshtein
/// </summary>
public class EmbeddingDegradationTests
{
    [Fact]
    public async Task FindMatches_WhenEmbeddingFails_ShouldDegradeToLevenshtein()
    {
        // 使用会抛异常的 Embedding 服务
        var failingEmbedding = new FailingEmbeddingService();
        var textSimilarity = new TextSimilarityService();
        var logger = NullLogger<SemanticKernelMatchingService>.Instance;

        var service = new SemanticKernelMatchingService(failingEmbedding, textSimilarity, logger);

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
        var results = await service.FindMatchesAsync("项目A 规格A", candidates, config);

        // 应返回降级结果
        results.Should().NotBeEmpty();
        var result = results[0];
        result.IsDegraded.Should().BeTrue();
        result.ScoreDetails.Should().ContainKey("Levenshtein");
        result.ScoreDetails["Levenshtein"].Should().BeGreaterThan(0);
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
