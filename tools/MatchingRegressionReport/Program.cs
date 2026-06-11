using System.Text.Json;
using System.Text.Json.Serialization;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using Microsoft.Extensions.Logging.Abstractions;

var inputPath = GetArg(args, "--input", Path.Combine("tests", "AcceptanceSpecSystem.Core.Tests", "Fixtures", "EvidenceDrivenMatchingBaseline.json"));
var outputPath = GetArg(args, "--output", string.Empty);
var minScore = double.Parse(GetArg(args, "--min-score", "0"));
var highConfidence = double.Parse(GetArg(args, "--high-confidence", "0.95"));

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"样本文件不存在: {inputPath}");
    return 2;
}

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};

var cases = JsonSerializer.Deserialize<List<BaselineCase>>(
    await File.ReadAllTextAsync(inputPath),
    jsonOptions) ?? [];

var rows = new List<ReportRow>();
foreach (var item in cases)
{
    var service = new SemanticKernelMatchingService(
        new BaselineEmbeddingService(item.Source.CombinedText, item.Candidates),
        NullLogger<SemanticKernelMatchingService>.Instance);

    var result = await service.BatchMatchAsync(
        [item.Source],
        item.Candidates.Select(candidate => new MatchCandidate
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
            MinScoreThreshold = minScore,
            HighConfidenceThreshold = highConfidence,
            RecallTopK = Math.Max(1, item.Candidates.Count),
            AmbiguityMargin = 0.01
        });

    var match = result.Results.Single();
    var expectedDecision = Enum.Parse<MatchDecision>(item.ExpectedDecision, ignoreCase: true);
    var pass = match.MatchedSpecId == item.ExpectedMatchedSpecId &&
               match.Decision == expectedDecision &&
               (string.IsNullOrWhiteSpace(item.ExpectedIssueCode) ||
                match.Issues.Any(issue => issue.Code.Equals(item.ExpectedIssueCode, StringComparison.OrdinalIgnoreCase)));

    rows.Add(new ReportRow(
        item.Name,
        pass,
        item.ExpectedMatchedSpecId,
        match.MatchedSpecId,
        item.ExpectedDecision,
        match.Decision.ToString(),
        match.Score,
        match.EmbeddingScore,
        string.Join("|", match.Issues.Select(issue => issue.Code).Distinct(StringComparer.OrdinalIgnoreCase)),
        string.Join("|", match.Evidence.Conflicts)));
}

var passed = rows.Count(row => row.Pass);
var failed = rows.Count - passed;
Console.WriteLine($"样本数: {rows.Count}, 通过: {passed}, 失败: {failed}");

foreach (var row in rows)
{
    Console.WriteLine(
        $"{(row.Pass ? "PASS" : "FAIL")}\t{row.Name}\tmatched {row.ActualMatchedSpecId?.ToString() ?? "null"}\tdecision {row.ActualDecision}\tissues {row.IssueCodes}");
}

if (!string.IsNullOrWhiteSpace(outputPath))
{
    var resolvedOutput = Path.GetFullPath(outputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutput) ?? ".");
    await File.WriteAllTextAsync(
        resolvedOutput,
        JsonSerializer.Serialize(rows, jsonOptions));
    Console.WriteLine($"报告已写入: {resolvedOutput}");
}

return failed == 0 ? 0 : 1;

static string GetArg(string[] args, string name, string fallback)
{
    var index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}

public sealed class BaselineCase
{
    public string Name { get; set; } = string.Empty;

    public MatchSource Source { get; set; } = new();

    public List<BaselineCandidate> Candidates { get; set; } = [];

    public int? ExpectedMatchedSpecId { get; set; }

    public string ExpectedDecision { get; set; } = nameof(MatchDecision.ManualReview);

    public string? ExpectedIssueCode { get; set; }
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

public sealed record ReportRow(
    string Name,
    bool Pass,
    int? ExpectedMatchedSpecId,
    int? ActualMatchedSpecId,
    string ExpectedDecision,
    string ActualDecision,
    double Score,
    double EmbeddingScore,
    string IssueCodes,
    string Conflicts);

internal sealed class BaselineEmbeddingService : IEmbeddingService
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

    public Task<float[]> GenerateEmbeddingAsync(
        string text,
        int? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(text, _sourceText, StringComparison.Ordinal))
            return Task.FromResult(new[] { 1f });

        return Task.FromResult(_candidateEmbeddings.TryGetValue(text, out var embedding) ? embedding : [0f]);
    }

    public Task<List<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        int? serviceId = null,
        CancellationToken cancellationToken = default)
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
