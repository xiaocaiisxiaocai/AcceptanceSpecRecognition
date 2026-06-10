using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Api.Tests;

public class ImportDuplicateDetectionServiceAvailabilityTests
{
    [Fact]
    public async Task CreateSessionAsync_WhenSemanticCheckDisabled_ShouldNotCallAiService()
    {
        var embeddingService = new RecordingEmbeddingService();
        var service = new ImportDuplicateDetectionService(
            embeddingService,
            new NoopLlmReviewService(),
            NullLogger<ImportDuplicateDetectionService>.Instance);

        var session = await service.CreateSessionAsync(
            [CreateSpec(1, "项目A", "规格A")],
            new ImportDuplicateCheckOptions
            {
                EnableSemanticDuplicateCheck = false
            });

        session.IsEnabled.Should().BeFalse();
        embeddingService.BatchCallCount.Should().Be(0);
        embeddingService.SingleCallCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateSessionAsync_WhenEmbeddingUnavailable_ShouldThrowUnavailableException()
    {
        var service = new ImportDuplicateDetectionService(
            new FailingEmbeddingService(failBatch: true),
            new NoopLlmReviewService(),
            NullLogger<ImportDuplicateDetectionService>.Instance);

        var act = async () => await service.CreateSessionAsync(
            [CreateSpec(1, "项目A", "规格A")],
            new ImportDuplicateCheckOptions
            {
                EnableSemanticDuplicateCheck = true
            });

        await act.Should().ThrowAsync<AiServiceUnavailableException>()
            .WithMessage("*Embedding*不可用*");
    }

    [Fact]
    public async Task DetectAsync_WhenLlmUnavailable_ShouldThrowUnavailableException()
    {
        var service = new ImportDuplicateDetectionService(
            new RecordingEmbeddingService(),
            new FailingLlmReviewService(),
            NullLogger<ImportDuplicateDetectionService>.Instance);

        var session = await service.CreateSessionAsync(
            [CreateSpec(1, "项目A", "规格A")],
            new ImportDuplicateCheckOptions
            {
                EnableSemanticDuplicateCheck = true,
                SemanticMinScore = 0.1,
                EnableLlmDuplicateReview = true
            });

        var act = async () => await session.DetectAsync("项目A", "规格A");

        await act.Should().ThrowAsync<AiServiceUnavailableException>()
            .WithMessage("*LLM*");
    }

    [Fact]
    public async Task DetectAsync_WhenLlmReviewEnabled_ShouldPassImportDuplicateReviewScene()
    {
        var llmReviewService = new RecordingLlmReviewService();
        var service = new ImportDuplicateDetectionService(
            new RecordingEmbeddingService(),
            llmReviewService,
            NullLogger<ImportDuplicateDetectionService>.Instance);

        var session = await service.CreateSessionAsync(
            [CreateSpec(1, "平台吸附精度", "平台平面度需控制在0.05mm以内")],
            new ImportDuplicateCheckOptions
            {
                EnableSemanticDuplicateCheck = true,
                SemanticMinScore = 0.1,
                EnableLlmDuplicateReview = true,
                LlmPassScore = 0.1
            });

        var match = await session.DetectAsync("平台精度", "平面度控制在0.05mm以内");

        match.Should().NotBeNull();
        llmReviewService.LastRequest.Should().NotBeNull();
        llmReviewService.LastRequest!.ReviewScene.Should().Be(LlmReviewScene.ImportDuplicateReview);
    }

    [Fact]
    public async Task RefreshCandidateAsync_WhenQueryEmbeddingAlreadyCached_ShouldReuseCacheWithoutSecondEmbeddingCall()
    {
        var embeddingService = new FailOnSecondSingleEmbeddingService();
        var service = new ImportDuplicateDetectionService(
            embeddingService,
            new NoopLlmReviewService(),
            NullLogger<ImportDuplicateDetectionService>.Instance);

        var existingSpec = CreateSpec(1, "旧项目", "旧规格");
        var session = await service.CreateSessionAsync(
            [existingSpec],
            new ImportDuplicateCheckOptions
            {
                EnableSemanticDuplicateCheck = true,
                SemanticMinScore = 0.1,
                EnableLlmDuplicateReview = false
            });

        var match = await session.DetectAsync("新项目", "新规格");
        match.Should().NotBeNull();
        embeddingService.SingleCallCount.Should().Be(1);

        existingSpec.Project = "新项目";
        existingSpec.Specification = "新规格";

        var act = async () => await session.RefreshCandidateAsync(existingSpec);

        await act.Should().NotThrowAsync();
        embeddingService.SingleCallCount.Should().Be(1);
    }

    private static AcceptanceSpec CreateSpec(int id, string project, string specification)
    {
        return new AcceptanceSpec
        {
            Id = id,
            CustomerId = 1,
            ProcessId = 1,
            Project = project,
            Specification = specification,
            Acceptance = "验收",
            Remark = "备注",
            WordFileId = 1,
            OwnerOrgUnitId = 1,
            CreatedByUserId = 1,
            ImportedAt = DateTime.UtcNow
        };
    }

    private static float[] CreateVector(string text)
    {
        var value = text ?? string.Empty;
        var vector = new float[8];

        for (var index = 0; index < value.Length; index++)
        {
            vector[index % vector.Length] += value[index];
        }

        var norm = (float)Math.Sqrt(vector.Sum(item => item * item));
        if (norm <= 0)
        {
            return vector;
        }

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] /= norm;
        }

        return vector;
    }

    private sealed class RecordingEmbeddingService : IEmbeddingService
    {
        public int BatchCallCount { get; private set; }

        public int SingleCallCount { get; private set; }

        public bool IsAvailable => true;

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            SingleCallCount++;
            return Task.FromResult(CreateVector(text));
        }

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            BatchCallCount++;
            return Task.FromResult(texts.Select(CreateVector).ToList());
        }

        public double ComputeSimilarity(float[] embedding1, float[] embedding2)
        {
            if (embedding1.Length == 0 || embedding2.Length == 0 || embedding1.Length != embedding2.Length)
            {
                return 0;
            }

            double dot = 0;
            double norm1 = 0;
            double norm2 = 0;
            for (var index = 0; index < embedding1.Length; index++)
            {
                dot += embedding1[index] * embedding2[index];
                norm1 += embedding1[index] * embedding1[index];
                norm2 += embedding2[index] * embedding2[index];
            }

            if (norm1 <= 0 || norm2 <= 0)
            {
                return 0;
            }

            return Math.Clamp(dot / (Math.Sqrt(norm1) * Math.Sqrt(norm2)), 0, 1);
        }

    }

    private sealed class FailingEmbeddingService : IEmbeddingService
    {
        private readonly bool _failBatch;

        public FailingEmbeddingService(bool failBatch)
        {
            _failBatch = failBatch;
        }

        public bool IsAvailable => false;

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
            => throw new AiServiceUnavailableException("Embedding 服务不可用");

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            if (_failBatch)
            {
                throw new AiServiceUnavailableException("Embedding 服务不可用");
            }

            return Task.FromResult(new List<float[]>());
        }

        public double ComputeSimilarity(float[] embedding1, float[] embedding2) => 0;
    }

    private sealed class FailOnSecondSingleEmbeddingService : IEmbeddingService
    {
        public int SingleCallCount { get; private set; }

        public bool IsAvailable => true;

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            SingleCallCount++;
            if (SingleCallCount >= 2)
            {
                throw new AiServiceUnavailableException("Embedding 服务不可用");
            }

            return Task.FromResult(CreateVector(text));
        }

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(texts.Select(CreateVector).ToList());
        }

        public double ComputeSimilarity(float[] embedding1, float[] embedding2)
        {
            if (embedding1.Length == 0 || embedding2.Length == 0 || embedding1.Length != embedding2.Length)
            {
                return 0;
            }

            double dot = 0;
            double norm1 = 0;
            double norm2 = 0;
            for (var index = 0; index < embedding1.Length; index++)
            {
                dot += embedding1[index] * embedding2[index];
                norm1 += embedding1[index] * embedding1[index];
                norm2 += embedding2[index] * embedding2[index];
            }

            if (norm1 <= 0 || norm2 <= 0)
            {
                return 0;
            }

            return Math.Clamp(dot / (Math.Sqrt(norm1) * Math.Sqrt(norm2)), 0, 1);
        }
    }

    private sealed class NoopLlmReviewService : ILlmReviewService
    {
        public Task<LlmReviewResult?> ReviewAsync(LlmReviewRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult<LlmReviewResult?>(null);

        public async IAsyncEnumerable<string> ReviewStreamAsync(
            LlmReviewRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public bool TryParseReviewResult(string raw, out LlmReviewResult result)
        {
            result = null!;
            return false;
        }
    }

    private sealed class FailingLlmReviewService : ILlmReviewService
    {
        public Task<LlmReviewResult?> ReviewAsync(LlmReviewRequest request, CancellationToken cancellationToken = default)
            => throw new AiServiceUnavailableException("LLM 复核失败");

        public async IAsyncEnumerable<string> ReviewStreamAsync(
            LlmReviewRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public bool TryParseReviewResult(string raw, out LlmReviewResult result)
        {
            result = null!;
            return false;
        }
    }

    private sealed class RecordingLlmReviewService : ILlmReviewService
    {
        public LlmReviewRequest? LastRequest { get; private set; }

        public Task<LlmReviewResult?> ReviewAsync(LlmReviewRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult<LlmReviewResult?>(new LlmReviewResult
            {
                Score = 95,
                Reason = "匹配",
                Commentary = "测试"
            });
        }

        public async IAsyncEnumerable<string> ReviewStreamAsync(
            LlmReviewRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            await Task.CompletedTask;
            yield break;
        }

        public bool TryParseReviewResult(string raw, out LlmReviewResult result)
        {
            result = new LlmReviewResult
            {
                Score = 95
            };
            return true;
        }
    }
}
