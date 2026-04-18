using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Core.Tests;

public class EvidenceDrivenMatchingBaselineTests
{
    public static TheoryData<BaselineCase> Cases => LoadCases();

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task BatchMatch_ShouldMatchExpectedDecisionOnBaselineCases(BaselineCase baselineCase)
    {
        var service = new SemanticKernelMatchingService(
            new BaselineEmbeddingService(baselineCase.Source.CombinedText, baselineCase.Candidates),
            NullLogger<SemanticKernelMatchingService>.Instance);

        var result = await service.BatchMatchAsync(
            [baselineCase.Source],
            baselineCase.Candidates.Select(candidate => new MatchCandidate
            {
                SpecId = candidate.SpecId,
                Project = candidate.Project,
                Specification = candidate.Specification,
                Acceptance = candidate.Acceptance,
                Remark = candidate.Remark,
                Embedding = [candidate.EmbeddingScore]
            }),
            new MatchingConfig
            {
                MinScoreThreshold = 0.0,
                RecallTopK = Math.Max(1, baselineCase.Candidates.Count),
                AmbiguityMargin = 0.01
            });

        result.Results.Should().HaveCount(1);
        var match = result.Results[0];
        match.MatchedSpecId.Should().Be(baselineCase.ExpectedMatchedSpecId);
        match.Decision.Should().Be(Enum.Parse<MatchDecision>(baselineCase.ExpectedDecision, ignoreCase: true));

        if (!string.IsNullOrWhiteSpace(baselineCase.ExpectedEvidenceContains))
            match.Evidence.Summary.Should().Contain(item => item.Contains(baselineCase.ExpectedEvidenceContains, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(baselineCase.ExpectedConflictContains))
            match.Evidence.Conflicts.Should().Contain(item => item.Contains(baselineCase.ExpectedConflictContains, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(baselineCase.ExpectedIssueCode))
            match.Issues.Should().Contain(issue => issue.Code.Equals(baselineCase.ExpectedIssueCode, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(baselineCase.ExpectedIssueMessageContains))
            match.Issues.Should().Contain(issue => issue.Message.Contains(baselineCase.ExpectedIssueMessageContains, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(baselineCase.ExpectedSuggestedActionContains))
            match.Issues.Should().Contain(issue =>
                !string.IsNullOrWhiteSpace(issue.SuggestedAction) &&
                issue.SuggestedAction.Contains(baselineCase.ExpectedSuggestedActionContains, StringComparison.OrdinalIgnoreCase));
    }

    private static TheoryData<BaselineCase> LoadCases()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "EvidenceDrivenMatchingBaseline.json");
        var json = File.ReadAllText(filePath);
        var cases = JsonSerializer.Deserialize<List<BaselineCase>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        var data = new TheoryData<BaselineCase>();
        foreach (var item in cases)
            data.Add(item);

        return data;
    }

    public sealed class BaselineCase
    {
        public string Name { get; set; } = string.Empty;

        public MatchSource Source { get; set; } = new();

        public List<BaselineCandidate> Candidates { get; set; } = [];

        public int? ExpectedMatchedSpecId { get; set; }

        public string ExpectedDecision { get; set; } = nameof(MatchDecision.ManualReview);

        public string? ExpectedEvidenceContains { get; set; }

        public string? ExpectedConflictContains { get; set; }

        public string? ExpectedIssueCode { get; set; }

        public string? ExpectedIssueMessageContains { get; set; }

        public string? ExpectedSuggestedActionContains { get; set; }

        public override string ToString() => Name;
    }

    public sealed class BaselineCandidate
    {
        public int SpecId { get; set; }

        public string Project { get; set; } = string.Empty;

        public string Specification { get; set; } = string.Empty;

        public string? Acceptance { get; set; }

        public string? Remark { get; set; }

        public float EmbeddingScore { get; set; }
    }

    private sealed class BaselineEmbeddingService : IEmbeddingService
    {
        private readonly string _sourceText;
        private readonly Dictionary<string, float[]> _candidateEmbeddings;

        public BaselineEmbeddingService(string sourceText, IEnumerable<BaselineCandidate> candidates)
        {
            _sourceText = sourceText;
            _candidateEmbeddings = candidates.ToDictionary(
                candidate => $"{candidate.Project} {candidate.Specification}".Trim(),
                candidate => new[] { candidate.EmbeddingScore },
                StringComparer.Ordinal);
        }

        public bool IsAvailable => true;

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            if (string.Equals(text, _sourceText, StringComparison.Ordinal))
                return Task.FromResult(new[] { 1f });

            return Task.FromResult(_candidateEmbeddings.TryGetValue(text, out var embedding) ? embedding : [0f]);
        }

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
        {
            var list = texts.Select(text =>
            {
                if (string.Equals(text, _sourceText, StringComparison.Ordinal))
                    return new[] { 1f };

                return _candidateEmbeddings.TryGetValue(text, out var embedding) ? embedding : [0f];
            }).ToList();

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
