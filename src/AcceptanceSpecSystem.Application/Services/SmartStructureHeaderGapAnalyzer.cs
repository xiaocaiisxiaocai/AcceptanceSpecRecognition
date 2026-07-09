using System.Text.Json;
using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 基于已确认模板反查列映射规则覆盖缺口；只用于离线统计，不参与运行时识别。
/// </summary>
public static class SmartStructureHeaderGapAnalyzer
{
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(200);

    public static SmartStructureHeaderGapReport Analyze(
        IEnumerable<DocumentTemplate> templates,
        IEnumerable<ColumnMappingRule> rules,
        int topN = 20)
    {
        var templateList = templates.ToList();
        var enabledRules = rules
            .Where(rule => rule.Enabled)
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Pattern))
            .ToList();

        var globalGaps = new Dictionary<HeaderGapKey, HeaderGapBucket>();
        var effectiveGaps = new Dictionary<HeaderGapKey, HeaderGapBucket>();
        var learnedGlobalCandidates = new Dictionary<HeaderGapKey, HeaderGapBucket>();
        var observationCount = 0;

        foreach (var template in templateList)
        {
            var headers = ParseHeaders(template.HeadersJson);
            foreach (var observation in EnumerateMappedHeaders(template, headers))
            {
                observationCount++;

                var globalRules = enabledRules.Where(rule =>
                    rule.CustomerId == null &&
                    rule.TargetField == observation.TargetField);
                if (!globalRules.Any(rule => IsMatch(rule, observation.Header)))
                {
                    Add(globalGaps, observation);
                }

                var effectiveRules = enabledRules.Where(rule =>
                    (rule.CustomerId == null || rule.CustomerId == observation.CustomerId) &&
                    rule.TargetField == observation.TargetField);
                if (!effectiveRules.Any(rule => IsMatch(rule, observation.Header)))
                {
                    Add(effectiveGaps, observation);
                }
            }
        }

        foreach (var rule in enabledRules.Where(rule =>
                     rule.Source == ColumnMappingRuleSource.Learned &&
                     rule.CustomerId.HasValue))
        {
            var globalRules = enabledRules.Where(candidate =>
                candidate.CustomerId == null &&
                candidate.TargetField == rule.TargetField);
            if (globalRules.Any(candidate => IsMatch(candidate, rule.Pattern)))
            {
                continue;
            }

            Add(
                learnedGlobalCandidates,
                new MappedHeaderObservation(
                    rule.Pattern.Trim(),
                    rule.TargetField,
                    rule.CustomerId!.Value,
                    TemplateId: 0,
                    TemplateName: "ColumnMappingRules",
                    rule.UpdatedAt ?? rule.CreatedAt));
        }

        var allGlobalUncoveredHeaders = ToItems(globalGaps, topN: 0);
        var allEffectiveUncoveredHeaders = ToItems(effectiveGaps, topN: 0);
        var allLearnedRuleGlobalCandidates = ToItems(learnedGlobalCandidates, topN: 0);
        var displayTakeCount = topN > 0 ? topN : int.MaxValue;
        var globalUncoveredHeaders = allGlobalUncoveredHeaders.Take(displayTakeCount).ToList();
        var effectiveUncoveredHeaders = allEffectiveUncoveredHeaders.Take(displayTakeCount).ToList();
        var learnedRuleGlobalCandidates = allLearnedRuleGlobalCandidates.Take(displayTakeCount).ToList();

        return new SmartStructureHeaderGapReport(
            TemplateCount: templateList.Count,
            MappedHeaderObservationCount: observationCount,
            GlobalUncoveredHeaders: globalUncoveredHeaders,
            EffectiveUncoveredHeaders: effectiveUncoveredHeaders,
            LearnedRuleGlobalCandidates: learnedRuleGlobalCandidates,
            Conclusion: BuildConclusion(
                observationCount,
                allGlobalUncoveredHeaders,
                allEffectiveUncoveredHeaders,
                allLearnedRuleGlobalCandidates));
    }

    private static IReadOnlyList<string> ParseHeaders(string? headersJson)
    {
        if (string.IsNullOrWhiteSpace(headersJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(headersJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IEnumerable<MappedHeaderObservation> EnumerateMappedHeaders(
        DocumentTemplate template,
        IReadOnlyList<string> headers)
    {
        if (!template.IsSpecificationOnly)
        {
            foreach (var item in TryCreate(template, headers, template.ProjectColumnIndex, ColumnMappingTargetField.Project))
            {
                yield return item;
            }
        }

        foreach (var item in TryCreate(template, headers, template.SpecificationColumnIndex, ColumnMappingTargetField.Specification))
        {
            yield return item;
        }

        foreach (var item in TryCreate(template, headers, template.AcceptanceColumnIndex, ColumnMappingTargetField.Acceptance))
        {
            yield return item;
        }

        foreach (var item in TryCreate(template, headers, template.RemarkColumnIndex, ColumnMappingTargetField.Remark))
        {
            yield return item;
        }
    }

    private static IEnumerable<MappedHeaderObservation> TryCreate(
        DocumentTemplate template,
        IReadOnlyList<string> headers,
        int? columnIndex,
        ColumnMappingTargetField targetField)
    {
        if (!columnIndex.HasValue || columnIndex.Value < 0 || columnIndex.Value >= headers.Count)
        {
            yield break;
        }

        var header = headers[columnIndex.Value].Trim();
        if (header.Length == 0)
        {
            yield break;
        }

        yield return new MappedHeaderObservation(
            header,
            targetField,
            template.CustomerId,
            template.Id,
            template.TemplateName,
            template.ConfirmedAt ?? template.UpdatedAt);
    }

    private static bool IsMatch(ColumnMappingRule rule, string header)
    {
        var pattern = rule.Pattern.Trim();
        if (pattern.Length == 0)
        {
            return false;
        }

        return rule.MatchMode switch
        {
            ColumnMappingMatchMode.Equals => string.Equals(header.Trim(), pattern, StringComparison.OrdinalIgnoreCase),
            ColumnMappingMatchMode.Regex => RegexMatches(header, pattern),
            _ => header.Contains(pattern, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool RegexMatches(string header, string pattern)
    {
        try
        {
            return Regex.IsMatch(
                header,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexMatchTimeout);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static void Add(
        Dictionary<HeaderGapKey, HeaderGapBucket> buckets,
        MappedHeaderObservation observation)
    {
        var key = new HeaderGapKey(NormalizeKey(observation.Header), observation.TargetField);
        if (!buckets.TryGetValue(key, out var bucket))
        {
            bucket = new HeaderGapBucket(observation.Header, observation.TargetField);
            buckets[key] = bucket;
        }

        bucket.Add(observation);
    }

    private static string NormalizeKey(string value) => value.Trim().ToUpperInvariant();

    private static IReadOnlyList<SmartStructureHeaderGapItem> ToItems(
        Dictionary<HeaderGapKey, HeaderGapBucket> buckets,
        int topN)
    {
        var takeCount = topN > 0 ? topN : int.MaxValue;
        return buckets.Values
            .Select(bucket => bucket.ToItem())
            .OrderByDescending(item => item.OccurrenceCount)
            .ThenByDescending(item => item.CustomerCount)
            .ThenBy(item => item.Header, StringComparer.OrdinalIgnoreCase)
            .Take(takeCount)
            .ToList();
    }

    private static SmartStructureHeaderGapConclusion BuildConclusion(
        int observationCount,
        IReadOnlyList<SmartStructureHeaderGapItem> globalUncoveredHeaders,
        IReadOnlyList<SmartStructureHeaderGapItem> effectiveUncoveredHeaders,
        IReadOnlyList<SmartStructureHeaderGapItem> learnedRuleGlobalCandidates)
    {
        var ruleBackfillCandidateCount = globalUncoveredHeaders.Count(IsRepeatableSignal);
        var learnedRulePromotionCandidateCount = learnedRuleGlobalCandidates.Count(IsRepeatableSignal);
        var customerRuleCandidateCount = effectiveUncoveredHeaders.Count(item => item.CustomerCount == 1);
        var hasMappingSignals = observationCount > 0 || learnedRuleGlobalCandidates.Count > 0;
        var nextAction = GetNextAction(
            hasMappingSignals,
            ruleBackfillCandidateCount,
            learnedRulePromotionCandidateCount,
            effectiveUncoveredHeaders.Count);

        return new SmartStructureHeaderGapConclusion(
            HasMappingSignals: hasMappingSignals,
            RuleBackfillCandidateCount: ruleBackfillCandidateCount,
            CustomerRuleCandidateCount: customerRuleCandidateCount,
            LearnedRulePromotionCandidateCount: learnedRulePromotionCandidateCount,
            EffectiveRuntimeGapCount: effectiveUncoveredHeaders.Count,
            NextAction: nextAction);
    }

    private static bool IsRepeatableSignal(SmartStructureHeaderGapItem item) =>
        item.CustomerCount >= 2 || item.OccurrenceCount >= 2;

    private static SmartStructureHeaderGapNextAction GetNextAction(
        bool hasMappingSignals,
        int ruleBackfillCandidateCount,
        int learnedRulePromotionCandidateCount,
        int effectiveRuntimeGapCount)
    {
        if (!hasMappingSignals)
        {
            return SmartStructureHeaderGapNextAction.CollectSamples;
        }

        if (ruleBackfillCandidateCount > 0 || learnedRulePromotionCandidateCount > 0)
        {
            return SmartStructureHeaderGapNextAction.ReviewRuleBackfillFirst;
        }

        return effectiveRuntimeGapCount > 0
            ? SmartStructureHeaderGapNextAction.ReviewCustomerRulesOrCollectMoreSamples
            : SmartStructureHeaderGapNextAction.NoAdditionalAction;
    }

    private sealed record HeaderGapKey(string NormalizedHeader, ColumnMappingTargetField TargetField);

    private sealed record MappedHeaderObservation(
        string Header,
        ColumnMappingTargetField TargetField,
        int CustomerId,
        int TemplateId,
        string TemplateName,
        DateTime LastConfirmedAt);

    private sealed class HeaderGapBucket
    {
        private readonly HashSet<int> _customerIds = [];
        private readonly HashSet<int> _templateIds = [];
        private readonly List<string> _exampleTemplateNames = [];
        private DateTime? _lastConfirmedAt;

        public HeaderGapBucket(string header, ColumnMappingTargetField targetField)
        {
            Header = header;
            TargetField = targetField;
        }

        private string Header { get; }
        private ColumnMappingTargetField TargetField { get; }
        private int OccurrenceCount { get; set; }

        public void Add(MappedHeaderObservation observation)
        {
            OccurrenceCount++;
            _customerIds.Add(observation.CustomerId);
            _templateIds.Add(observation.TemplateId);
            if (_exampleTemplateNames.Count < 3 &&
                !_exampleTemplateNames.Contains(observation.TemplateName, StringComparer.OrdinalIgnoreCase))
            {
                _exampleTemplateNames.Add(observation.TemplateName);
            }

            if (!_lastConfirmedAt.HasValue || observation.LastConfirmedAt > _lastConfirmedAt.Value)
            {
                _lastConfirmedAt = observation.LastConfirmedAt;
            }
        }

        public SmartStructureHeaderGapItem ToItem() => new(
            Header,
            TargetField,
            OccurrenceCount,
            _customerIds.Count,
            _customerIds.Order().ToList(),
            _templateIds.Order().ToList(),
            _exampleTemplateNames.ToList(),
            _lastConfirmedAt);
    }
}

public sealed record SmartStructureHeaderGapReport(
    int TemplateCount,
    int MappedHeaderObservationCount,
    IReadOnlyList<SmartStructureHeaderGapItem> GlobalUncoveredHeaders,
    IReadOnlyList<SmartStructureHeaderGapItem> EffectiveUncoveredHeaders,
    IReadOnlyList<SmartStructureHeaderGapItem> LearnedRuleGlobalCandidates,
    SmartStructureHeaderGapConclusion Conclusion);

public sealed record SmartStructureHeaderGapItem(
    string Header,
    ColumnMappingTargetField TargetField,
    int OccurrenceCount,
    int CustomerCount,
    IReadOnlyList<int> CustomerIds,
    IReadOnlyList<int> TemplateIds,
    IReadOnlyList<string> ExampleTemplateNames,
    DateTime? LastConfirmedAt);

public sealed record SmartStructureHeaderGapConclusion(
    bool HasMappingSignals,
    int RuleBackfillCandidateCount,
    int CustomerRuleCandidateCount,
    int LearnedRulePromotionCandidateCount,
    int EffectiveRuntimeGapCount,
    SmartStructureHeaderGapNextAction NextAction);

public enum SmartStructureHeaderGapNextAction
{
    CollectSamples = 1,
    ReviewRuleBackfillFirst = 2,
    ReviewCustomerRulesOrCollectMoreSamples = 3,
    NoAdditionalAction = 4
}
