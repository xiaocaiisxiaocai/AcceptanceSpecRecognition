using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel.ChatCompletion;

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
                Specification = "规格A-候选",
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

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenRequestCancelled_ShouldPropagateOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var service = new SemanticKernelEmbeddingService(
            new StaticAiServiceSelector(),
            new CancelOnRequestEmbeddingFactory(),
            NullLogger<SemanticKernelEmbeddingService>.Instance);

        var act = async () => await service.GenerateEmbeddingAsync("项目A\n规格A", cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_WhenRequestCancelled_ShouldPropagateOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var service = new SemanticKernelEmbeddingService(
            new StaticAiServiceSelector(),
            new CancelOnRequestEmbeddingFactory(),
            NullLogger<SemanticKernelEmbeddingService>.Instance);

        var act = async () => await service.GenerateEmbeddingsAsync(["项目A\n规格A"], cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
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

    private sealed class StaticAiServiceSelector : IAiServiceSelector
    {
        public Task<IReadOnlyList<AiServiceConfigModel>> GetCandidatesAsync(
            AiServicePurpose purpose,
            int? preferredId = null,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AiServiceConfigModel> result =
            [
                new AiServiceConfigModel
                {
                    Id = 1,
                    Name = "Embedding-1",
                    ServiceType = AiServiceType.OpenAI,
                    Purpose = AiServicePurpose.Embedding,
                    EmbeddingModel = "text-embedding-3-small"
                }
            ];
            return Task.FromResult(result);
        }
    }

    private sealed class CancelOnRequestEmbeddingFactory : ISemanticKernelServiceFactory
    {
        public Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService CreateChatCompletionService(AiServiceConfigModel config)
        {
            throw new NotSupportedException();
        }

        public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiServiceConfigModel config)
        {
            return new CancelOnRequestEmbeddingGenerator();
        }
    }

    private sealed class CancelOnRequestEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new GeneratedEmbeddings<Embedding<float>>([]);
        }
    }
}
