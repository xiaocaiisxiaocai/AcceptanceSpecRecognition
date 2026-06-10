using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// 构建源项与候选项之间的结构化证据。
/// 型号/料号冲突由内置正则检测；数值/单位、比较符、温度跨温标、方向/极性反义
/// 等"有规律的硬冲突"交由 <see cref="SemanticConflictScanner"/> 确定性检测，
/// 不再依赖 LLM 裁决。仅极少数字典/规则覆盖不到的长尾才交由 Embedding + LLM 兜底。
/// </summary>
public sealed class MatchEvidenceBuilder : IMatchEvidenceBuilder
{
    private static readonly Regex IdentifierRegex = new(
        @"\b[A-Z]{2,}(?:-[A-Z0-9]+)+\b",
        RegexOptions.Compiled);

    private readonly SemanticConflictScanner? _conflictScanner;

    /// <summary>
    /// 默认构造：仅做型号/料号证据，保持向后兼容。
    /// </summary>
    public MatchEvidenceBuilder()
    {
    }

    /// <summary>
    /// 注入语义冲突扫描器后，额外检测数值/单位、比较符、温度、方向极性硬冲突。
    /// </summary>
    public MatchEvidenceBuilder(SemanticConflictScanner? conflictScanner)
    {
        _conflictScanner = conflictScanner;
    }

    public MatchEvidence Build(MatchSource source, MatchCandidate candidate)
    {
        var evidence = new MatchEvidence();
        BuildIdentifierEvidence(evidence, source, candidate);
        _conflictScanner?.Scan(evidence, source, candidate);
        return evidence;
    }

    private void BuildIdentifierEvidence(MatchEvidence evidence, MatchSource source, MatchCandidate candidate)
    {
        var sourceIdentifiers = ExtractIdentifiers($"{source.Project} {source.Specification}");
        var candidateIdentifiers = ExtractIdentifiers($"{candidate.Project} {candidate.Specification}");
        if (sourceIdentifiers.Count == 0 || candidateIdentifiers.Count == 0)
            return;

        var remainingCandidates = new List<string>(candidateIdentifiers);
        foreach (var sourceIdentifier in sourceIdentifiers)
        {
            var exactIndex = remainingCandidates.FindIndex(candidateIdentifier =>
                candidateIdentifier.Equals(sourceIdentifier, StringComparison.OrdinalIgnoreCase));
            if (exactIndex >= 0)
            {
                var candidateIdentifier = remainingCandidates[exactIndex];
                remainingCandidates.RemoveAt(exactIndex);
                evidence.Identifiers.Add(new IdentifierEvidence
                {
                    SourceValue = sourceIdentifier,
                    CandidateValue = candidateIdentifier,
                    Relation = EvidenceRelation.Exact
                });
                evidence.Summary.Add($"型号一致：{sourceIdentifier}");
                continue;
            }

            var familyIndex = remainingCandidates.FindIndex(candidateIdentifier =>
                BelongsToSameIdentifierFamily(sourceIdentifier, candidateIdentifier));
            if (familyIndex < 0)
                continue;

            var conflictingCandidate = remainingCandidates[familyIndex];
            remainingCandidates.RemoveAt(familyIndex);

            evidence.Identifiers.Add(new IdentifierEvidence
            {
                SourceValue = sourceIdentifier,
                CandidateValue = conflictingCandidate,
                Relation = EvidenceRelation.Conflict
            });
            evidence.Conflicts.Add($"型号冲突：{sourceIdentifier} vs {conflictingCandidate}");
            evidence.Issues.Add(new MatchIssue
            {
                Code = "identifier_conflict",
                Severity = "high",
                FieldName = "型号",
                SourceValue = sourceIdentifier,
                CandidateValue = conflictingCandidate,
                Message = BuildIdentifierConflictMessage(sourceIdentifier, conflictingCandidate),
                SuggestedAction = "请人工确认型号/料号，避免使用错误物料"
            });
        }

        if (evidence.Identifiers.Count == 0 && sourceIdentifiers.Count == 1 && candidateIdentifiers.Count == 1)
        {
            evidence.Identifiers.Add(new IdentifierEvidence
            {
                SourceValue = sourceIdentifiers[0],
                CandidateValue = candidateIdentifiers[0],
                Relation = EvidenceRelation.Conflict
            });
            evidence.Conflicts.Add($"型号冲突：{sourceIdentifiers[0]} vs {candidateIdentifiers[0]}");
            evidence.Issues.Add(new MatchIssue
            {
                Code = "identifier_conflict",
                Severity = "high",
                FieldName = "型号",
                SourceValue = sourceIdentifiers[0],
                CandidateValue = candidateIdentifiers[0],
                Message = BuildIdentifierConflictMessage(sourceIdentifiers[0], candidateIdentifiers[0]),
                SuggestedAction = "请人工确认型号/料号，避免使用错误物料"
            });
        }
    }

    private static string BuildIdentifierConflictMessage(string sourceValue, string candidateValue)
    {
        return $"型号/料号不一致：源项为 {sourceValue}，候选为 {candidateValue}，无法自动采用";
    }

    private static List<string> ExtractIdentifiers(string text)
    {
        return IdentifierRegex.Matches(text ?? string.Empty)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool BelongsToSameIdentifierFamily(string sourceIdentifier, string candidateIdentifier)
    {
        var sourceFamily = GetIdentifierFamily(sourceIdentifier);
        var candidateFamily = GetIdentifierFamily(candidateIdentifier);
        return !string.IsNullOrWhiteSpace(sourceFamily) &&
               sourceFamily.Equals(candidateFamily, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetIdentifierFamily(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return string.Empty;

        var segments = identifier.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length <= 1)
            return segments.Length == 1 ? segments[0] : string.Empty;

        return string.Join('-', segments[..^1]);
    }
}
