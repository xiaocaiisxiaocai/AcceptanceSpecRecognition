using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using Microsoft.Extensions.Logging;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public partial class SemanticKernelMatchingService : IMatchingService
{
    private Dictionary<string, List<MatchCandidate>> BuildExactMatchLookup(
        IEnumerable<MatchCandidate> candidateList,
        MatchingConfig config)
    {
        var lookup = new Dictionary<string, List<MatchCandidate>>(StringComparer.Ordinal);
        foreach (var candidate in candidateList)
        {
            var key = BuildExactMatchKey(candidate.Project, candidate.Specification, config);
            if (!lookup.TryGetValue(key, out var list))
            {
                list = [];
                lookup[key] = list;
            }

            list.Add(candidate);
        }

        SortShortcutCandidates(lookup);
        return lookup;
    }

    private bool TryBuildExactMatchResult(
        MatchSource source,
        IReadOnlyDictionary<string, List<MatchCandidate>> exactMatchLookup,
        MatchingConfig config,
        out MatchResult result)
    {
        var key = BuildExactMatchKey(source.Project, source.Specification, config);
        if (!exactMatchLookup.TryGetValue(key, out var candidatesForKey))
        {
            result = null!;
            return false;
        }

        var candidate = candidatesForKey[0];
        var isSpecificationOnly = config.MatchingMode == MatchingMode.SpecificationOnly;
        var isAmbiguous = isSpecificationOnly && candidatesForKey.Count > 1;

        var exactCandidate = new EvaluatedCandidate
        {
            Source = source,
            Candidate = candidate,
            EmbeddingScore = 1.0,
            ProjectScore = isSpecificationOnly ? 0 : 1.0,
            SpecificationTextScore = 1.0,
            FinalScore = 1.0
        };

        exactCandidate.Evidence = _evidenceBuilder.Build(source, candidate);
        exactCandidate.NumericScore = ComputeNumericScore(source, exactCandidate);
        exactCandidate.Issues = BuildCandidateIssues(source, exactCandidate);
        exactCandidate.FinalScore = ComputeFinalScore(exactCandidate);
        exactCandidate.LlmEquivalence = CreateExactMatchEquivalenceResult(config);
        exactCandidate.SelectionMode = MatchSelectionMode.ExactShortcut;
        exactCandidate.SelectionSummary = isSpecificationOnly
            ? isAmbiguous
                ? "规格精确一致，但同规格存在多条候选，需人工确认"
                : "规格精确一致，按仅规格模式直接命中"
            : "项目与规格精确一致，直接命中";
        exactCandidate.MatchBasis = isSpecificationOnly
            ? MatchBasis.Specification
            : MatchBasis.ProjectSpecification;
        exactCandidate.RerankSummary = AppendEquivalenceSummary(
            BuildRerankSummary(exactCandidate),
            exactCandidate.LlmEquivalence);

        var orderedCandidates = isSpecificationOnly
            ? BuildShortcutCandidateSnapshots(source, candidatesForKey, exactCandidate, config)
            : [exactCandidate];

        result = BuildMatchResult(
            exactCandidate,
            recalledCandidateCount: isSpecificationOnly ? candidatesForKey.Count : 1,
            isAmbiguous,
            scoreGap: null,
            config.MinScoreThreshold,
            config.HighConfidenceThreshold,
            config,
            orderedCandidates);
        return true;
    }

    private static LlmEquivalenceAdjudicationResult CreateExactMatchEquivalenceResult(MatchingConfig config)
    {
        return new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 1,
            Reason = config.MatchingMode == MatchingMode.SpecificationOnly
                ? "规格文本完全一致，已按用户选择的仅规格模式命中"
                : "项目与规格文本完全一致，已直接视为等价"
        };
    }

    /// <summary>
    /// 构建规范化精确匹配查找表。
    /// 键 = Canonicalize(项目)+Canonicalize(规格)，可吸收单位/品牌/同义/格式差异。
    /// 原文精确层会先执行；规范化层作为第二层补充，用于命中原文不完全一致但规范化后等价的候选。
    /// </summary>
    private Dictionary<string, List<MatchCandidate>> BuildCanonicalMatchLookup(
        IEnumerable<MatchCandidate> candidateList,
        MatchingConfig config)
    {
        var lookup = new Dictionary<string, List<MatchCandidate>>(StringComparer.Ordinal);
        foreach (var candidate in candidateList)
        {
            var key = BuildCanonicalMatchKey(candidate.Project, candidate.Specification, config);
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (!lookup.TryGetValue(key, out var list))
            {
                list = [];
                lookup[key] = list;
            }

            list.Add(candidate);
        }

        SortShortcutCandidates(lookup);
        return lookup;
    }

    /// <summary>
    /// 尝试用规范化精确层命中候选。命中后直接 AutoApply，跳过 Embedding 与 LLM。
    /// </summary>
    private bool TryBuildCanonicalMatchResult(
        MatchSource source,
        IReadOnlyDictionary<string, List<MatchCandidate>> canonicalMatchLookup,
        MatchingConfig config,
        out MatchResult result)
    {
        var key = BuildCanonicalMatchKey(source.Project, source.Specification, config);
        if (string.IsNullOrEmpty(key) || !canonicalMatchLookup.TryGetValue(key, out var candidatesForKey))
        {
            result = null!;
            return false;
        }

        var candidate = candidatesForKey[0];
        var isSpecificationOnly = config.MatchingMode == MatchingMode.SpecificationOnly;
        var isAmbiguous = isSpecificationOnly && candidatesForKey.Count > 1;

        var equivalence = new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 1,
            Reason = "规范化后等价（单位/品牌/同义/格式归一一致），已确定性命中"
        };

        var canonicalCandidate = new EvaluatedCandidate
        {
            Source = source,
            Candidate = candidate,
            EmbeddingScore = 1.0,
            ProjectScore = isSpecificationOnly ? 0 : 1.0,
            SpecificationTextScore = 1.0,
            FinalScore = 1.0
        };

        canonicalCandidate.Evidence = _evidenceBuilder.Build(source, candidate);
        canonicalCandidate.NumericScore = ComputeNumericScore(source, canonicalCandidate);
        canonicalCandidate.Issues = BuildCandidateIssues(source, canonicalCandidate);

        // 安全网：规范化命中后仍跑冲突扫描。理论上规范化等价不应有硬冲突，
        // 但若证据构建器扫出 hard_conflict（如数值归一边界差异），宁可转人工。
        if (HasHardConflict(canonicalCandidate.Issues))
        {
            result = null!;
            return false;
        }

        canonicalCandidate.FinalScore = ComputeFinalScore(canonicalCandidate);
        canonicalCandidate.LlmEquivalence = equivalence;
        canonicalCandidate.SelectionMode = MatchSelectionMode.ExactShortcut;
        canonicalCandidate.SelectionSummary = isSpecificationOnly && isAmbiguous
            ? "规范化后规格等价，但同规格存在多条候选，需人工确认"
            : "规范化等价（单位/品牌/同义/格式归一），确定性直接命中";
        canonicalCandidate.MatchBasis = isSpecificationOnly
            ? MatchBasis.Specification
            : MatchBasis.ProjectSpecification;
        canonicalCandidate.RerankSummary = AppendEquivalenceSummary(
            BuildRerankSummary(canonicalCandidate),
            equivalence);

        var orderedCandidates = isSpecificationOnly
            ? BuildShortcutCandidateSnapshots(source, candidatesForKey, canonicalCandidate, config)
            : [canonicalCandidate];

        result = BuildMatchResult(
            canonicalCandidate,
            recalledCandidateCount: isSpecificationOnly ? candidatesForKey.Count : 1,
            isAmbiguous,
            scoreGap: null,
            config.MinScoreThreshold,
            config.HighConfidenceThreshold,
            config,
            orderedCandidates);
        return true;
    }

    /// <summary>
    /// 候选项的近似规范化快照：规范化项目、规格骨架与可归一数值集合，整批预计算一次。
    /// </summary>
    private sealed record CandidateCanonicalSnapshot(
        MatchCandidate Candidate,
        string CanonicalProject,
        string SpecificationSkeleton,
        IReadOnlyList<(double BaseValue, string Dimension, string OriginalExpression)> NormalizedValues);

    private List<CandidateCanonicalSnapshot> BuildCandidateCanonicalSnapshots(
        IReadOnlyList<MatchCandidate> candidateList)
    {
        var snapshots = new List<CandidateCanonicalSnapshot>(candidateList.Count);
        foreach (var candidate in candidateList)
        {
            snapshots.Add(new CandidateCanonicalSnapshot(
                candidate,
                _canonicalizer.Canonicalize(candidate.Project),
                BuildCanonicalSpecificationSkeleton(candidate.Specification),
                _canonicalizer.ExtractNormalizedValues(candidate.Specification)));
        }

        return snapshots;
    }

    private bool TryBuildApproximateCanonicalMatchResult(
        MatchSource source,
        IReadOnlyList<CandidateCanonicalSnapshot> candidateSnapshots,
        MatchingConfig config,
        out MatchResult result)
    {
        result = null!;

        var sourceProject = config.MatchingMode == MatchingMode.SpecificationOnly
            ? null
            : _canonicalizer.Canonicalize(source.Project);

        var sourceValues = _canonicalizer.ExtractNormalizedValues(source.Specification);
        if (sourceValues.Count == 0)
            return false;

        var sourceSkeleton = BuildCanonicalSpecificationSkeleton(source.Specification);
        if (string.IsNullOrWhiteSpace(sourceSkeleton))
            return false;

        var candidatesForKey = candidateSnapshots
            .Where(snapshot =>
                (sourceProject == null ||
                 string.Equals(snapshot.CanonicalProject, sourceProject, StringComparison.Ordinal)) &&
                string.Equals(sourceSkeleton, snapshot.SpecificationSkeleton, StringComparison.Ordinal) &&
                NormalizedValueSetsEqual(sourceValues, snapshot.NormalizedValues))
            .Select(snapshot => snapshot.Candidate)
            .ToList();

        if (candidatesForKey.Count == 0)
            return false;

        SortShortcutCandidatesByList(candidatesForKey);
        var candidate = candidatesForKey[0];
        var isSpecificationOnly = config.MatchingMode == MatchingMode.SpecificationOnly;
        var approximateCandidate = new EvaluatedCandidate
        {
            Source = source,
            Candidate = candidate,
            EmbeddingScore = 1.0,
            ProjectScore = isSpecificationOnly ? 0 : 1.0,
            SpecificationTextScore = 1.0,
            NumericScore = 1.0,
            FinalScore = 1.0
        };

        approximateCandidate.Evidence = _evidenceBuilder.Build(source, candidate);
        approximateCandidate.Issues = BuildCandidateIssues(source, approximateCandidate);
        // 语义优先模式下，AutoApplyBlocking warning 不再拦截（与 DetermineDecision 的处理保持一致）
        if (HasHardConflict(approximateCandidate.Issues) ||
            (!config.EnableLlmSemanticPriority && HasAutoApplyBlockingWarning(approximateCandidate.Issues)))
            return false;

        approximateCandidate.LlmEquivalence = new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.SymbolEquivalent,
            Confidence = 1,
            Reason = "规范化数值在工程容差内等价，已确定性命中"
        };
        approximateCandidate.SelectionMode = MatchSelectionMode.ExactShortcut;
        approximateCandidate.SelectionSummary = isSpecificationOnly
            ? "规范化数值在工程容差内等价，已按仅规格模式确定性命中"
            : "规范化数值在工程容差内等价，确定性直接命中";
        approximateCandidate.MatchBasis = isSpecificationOnly
            ? MatchBasis.Specification
            : MatchBasis.ProjectSpecification;
        approximateCandidate.RerankSummary = AppendEquivalenceSummary(
            BuildRerankSummary(approximateCandidate),
            approximateCandidate.LlmEquivalence);

        result = BuildMatchResult(
            approximateCandidate,
            recalledCandidateCount: 1,
            isAmbiguous: false,
            scoreGap: null,
            config.MinScoreThreshold,
            config.HighConfidenceThreshold,
            config,
            [approximateCandidate]);
        return true;
    }

    private static void SortShortcutCandidates(Dictionary<string, List<MatchCandidate>> lookup)
    {
        foreach (var key in lookup.Keys.ToList())
        {
            SortShortcutCandidatesByList(lookup[key]);
        }
    }

    private static void SortShortcutCandidatesByList(List<MatchCandidate> candidates)
    {
        candidates.Sort((left, right) =>
        {
            var acceptance = HasText(right.Acceptance).CompareTo(HasText(left.Acceptance));
            if (acceptance != 0) return acceptance;
            var remark = HasText(right.Remark).CompareTo(HasText(left.Remark));
            if (remark != 0) return remark;
            return right.SpecId.CompareTo(left.SpecId);
        });
    }

    private List<EvaluatedCandidate> BuildShortcutCandidateSnapshots(
        MatchSource source,
        IReadOnlyList<MatchCandidate> candidates,
        EvaluatedCandidate primary,
        MatchingConfig config)
    {
        var snapshots = new List<EvaluatedCandidate> { primary };
        foreach (var candidate in candidates.Skip(1).Take(TopCandidateLimit - 1))
        {
            var evaluated = new EvaluatedCandidate
            {
                Source = source,
                Candidate = candidate,
                EmbeddingScore = 1.0,
                ProjectScore = config.MatchingMode == MatchingMode.SpecificationOnly ? 0 : 1.0,
                SpecificationTextScore = 1.0,
                NumericScore = 1.0,
                FinalScore = 1.0,
                SelectionMode = MatchSelectionMode.ExactShortcut,
                SelectionSummary = config.MatchingMode == MatchingMode.SpecificationOnly
                    ? "规格精确一致"
                    : "项目与规格精确一致，直接命中",
                MatchBasis = config.MatchingMode == MatchingMode.SpecificationOnly
                    ? MatchBasis.Specification
                    : MatchBasis.ProjectSpecification,
                Evidence = _evidenceBuilder.Build(source, candidate),
                LlmEquivalence = null
            };
            evaluated.Issues = BuildCandidateIssues(source, evaluated);
            evaluated.RerankSummary = BuildRerankSummary(evaluated);
            snapshots.Add(evaluated);
        }

        return snapshots;
    }

    /// <summary>
    /// 构建规范化匹配键。若项目与规格均为空，返回空字符串，由调用方跳过。
    /// </summary>
    private string BuildCanonicalMatchKey(
        string? project,
        string? specification,
        MatchingConfig config)
    {
        var canonicalProject = _canonicalizer.Canonicalize(project);
        var canonicalSpecification = _canonicalizer.Canonicalize(specification);

        var canonicalKey = config.MatchingMode == MatchingMode.SpecificationOnly
            ? canonicalSpecification
            : $"{canonicalProject}\n{canonicalSpecification}";

        if (string.IsNullOrWhiteSpace(canonicalKey.Replace("\n", string.Empty)))
        {
            return string.Empty;
        }

        return canonicalKey;
    }

    private static string BuildExactMatchKey(
        string? project,
        string? specification,
        MatchingConfig config)
    {
        if (config.MatchingMode == MatchingMode.SpecificationOnly)
        {
            return NormalizeComparableText(specification);
        }

        return $"{NormalizeComparableText(project)}\n{NormalizeComparableText(specification)}";
    }

}
