using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Application.Services;

public sealed record MatchingUserContext(
    int UserId,
    int CompanyId,
    string Username = "",
    bool IsAdmin = false);

public interface IMatchingApprovalTokenProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
}

public interface IMatchingEventStream
{
    bool IsClientDisconnected { get; }
    void Prepare();
    Task WriteEventAsync(string eventName, object data, CancellationToken cancellationToken);
}

public interface IMatchingResultWriteBackPort
{
    Task<RenderedWriteBackFile> RenderFillResultToSourceFileAsync(
        WordFile wordFile,
        FillTaskResult taskResult,
        CancellationToken cancellationToken = default);

    Task<byte[]> RenderFilledContentAsync(
        WordFile wordFile,
        FillTaskResult taskResult,
        CancellationToken cancellationToken = default);
}

public interface IMatchingEmbeddingCache
{
    Task HydrateMatchingCandidatesAsync(
        IReadOnlyCollection<MatchCandidate> candidates,
        int? embeddingServiceId,
        MatchingMode matchingMode = MatchingMode.ProjectSpecification,
        CancellationToken cancellationToken = default);
}

public interface ISpecSemanticEmbeddingCache
{
    Task<string?> ResolveEmbeddingModelNameAsync(
        int? embeddingServiceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpecEmbeddingResult>> GetOrCreateForSpecsAsync(
        IReadOnlyCollection<AcceptanceSpec> specs,
        string usage,
        int? embeddingServiceId,
        CancellationToken cancellationToken = default);
}

public sealed record SpecEmbeddingResult(int SpecId, string Text, float[] Embedding);
