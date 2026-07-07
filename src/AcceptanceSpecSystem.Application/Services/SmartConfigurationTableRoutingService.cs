using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

internal static class SmartConfigurationTableRoutingService
{
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(200);

    public static SmartConfigurationRecognizedTable Enrich(
        TableInfo? tableInfo,
        TableData tableData,
        SmartConfigurationRecognizedTable table,
        DocumentStructureHealthCheckResult? healthCheck,
        IReadOnlyList<SmartStructureRoutingRule> routingRules,
        double referenceCaseScore = 0)
    {
        var route = Route(tableInfo, tableData, table, healthCheck, routingRules, referenceCaseScore);
        return CopyWithRouting(table, route);
    }

    public static SmartConfigurationTableRoutingDecision Route(
        TableInfo? tableInfo,
        TableData tableData,
        SmartConfigurationRecognizedTable table,
        DocumentStructureHealthCheckResult? healthCheck,
        IReadOnlyList<SmartStructureRoutingRule> routingRules,
        double referenceCaseScore = 0)
    {
        var matchedRule = FindBestRule(tableInfo, tableData, table, routingRules);
        var mappedFieldScore = CalculateMappedFieldScore(table);
        var healthIssues = BuildHealthIssues(healthCheck);
        var issues = new List<SmartConfigurationRecognitionIssue>(healthIssues);

        var kind = matchedRule?.TableKind ?? InferStructuralTableKind(table, mappedFieldScore);
        var kindScore = GetKindScore(kind, matchedRule);
        var clampedReferenceScore = Math.Clamp(referenceCaseScore, 0, 1);
        var rankingScore = Math.Clamp(
            table.Confidence * 0.35 +
            mappedFieldScore * 0.30 +
            kindScore * 0.25 +
            clampedReferenceScore * 0.10,
            0,
            1);

        var recommendation = matchedRule?.Recommendation ?? GetDefaultRecommendation(table, healthCheck, mappedFieldScore, kindScore);
        string? skipReason = null;
        if (string.Equals(recommendation, "Skip", StringComparison.OrdinalIgnoreCase))
        {
            rankingScore = Math.Min(rankingScore, 0.35);
            skipReason = matchedRule == null
                ? "该表被建议跳过。"
                : $"命中路由规则“{matchedRule.Name}”，建议跳过。";
        }

        if (matchedRule != null)
        {
            issues.Insert(0, new SmartConfigurationRecognitionIssue
            {
                Code = $"RoutingRule.{matchedRule.Id}",
                Severity = "Info",
                Message = string.Equals(recommendation, "Skip", StringComparison.OrdinalIgnoreCase)
                    ? skipReason!
                    : $"命中路由规则“{matchedRule.Name}”。"
            });
        }

        if (healthIssues.Count > 0 && string.Equals(recommendation, "Recommended", StringComparison.OrdinalIgnoreCase))
        {
            recommendation = "NeedConfirm";
        }

        if (!string.Equals(recommendation, "Skip", StringComparison.OrdinalIgnoreCase) &&
            !table.RemarkColumnIndex.HasValue &&
            issues.All(issue => issue.Code != DocumentStructureHealthIssueCode.MissingRemarkColumn.ToString()))
        {
            issues.Add(new SmartConfigurationRecognitionIssue
            {
                Code = DocumentStructureHealthIssueCode.MissingRemarkColumn.ToString(),
                Severity = "Info",
                Field = "Remark",
                Message = "未识别备注列，确认后将不写入备注列"
            });
        }

        return new SmartConfigurationTableRoutingDecision(
            kind,
            recommendation,
            Math.Round(rankingScore, 2),
            skipReason,
            issues);
    }

    public static bool ShouldUseStructureAdjudication(
        TableInfo? tableInfo,
        TableData tableData,
        SmartConfigurationRecognizedTable table,
        DocumentStructureHealthCheckResult healthCheck,
        IReadOnlyList<SmartStructureRoutingRule> routingRules)
    {
        var route = Route(tableInfo, tableData, table, healthCheck, routingRules);
        if (route.Recommendation == "Skip")
        {
            return false;
        }

        if (!HasStructureHeaderSignal(table))
        {
            return false;
        }

        return table.AcceptanceColumnIndex.HasValue ||
               (table.ProjectColumnIndex.HasValue && table.SpecificationColumnIndex.HasValue);
    }

    private static SmartStructureRoutingRule? FindBestRule(
        TableInfo? tableInfo,
        TableData tableData,
        SmartConfigurationRecognizedTable table,
        IReadOnlyList<SmartStructureRoutingRule> routingRules)
    {
        return routingRules
            .Where(rule => MatchesRule(rule, tableInfo, tableData, table))
            .OrderByDescending(rule => rule.Priority)
            .ThenByDescending(rule => rule.Weight)
            .ThenBy(rule => rule.Id)
            .FirstOrDefault();
    }

    private static bool MatchesRule(
        SmartStructureRoutingRule rule,
        TableInfo? tableInfo,
        TableData tableData,
        SmartConfigurationRecognizedTable table)
    {
        var values = GetScopeValues(rule.MatchScope, tableInfo, tableData, table);
        return values.Any(value => MatchesPattern(value, rule.MatchMode, rule.Pattern));
    }

    private static IReadOnlyList<string> GetScopeValues(
        SmartStructureRoutingMatchScope scope,
        TableInfo? tableInfo,
        TableData tableData,
        SmartConfigurationRecognizedTable table)
    {
        var tableNames = new[] { tableInfo?.Name, table.TableName }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
        var headers = table.Headers.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        var rowValues = tableData.Rows
            .Take(3)
            .SelectMany(row => row.Cells)
            .Select(cell => cell.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();

        return scope switch
        {
            SmartStructureRoutingMatchScope.TableName => tableNames,
            SmartStructureRoutingMatchScope.Headers => headers,
            SmartStructureRoutingMatchScope.SampleRows => rowValues,
            _ => tableNames.Concat(headers).Concat(rowValues).ToList()
        };
    }

    private static bool MatchesPattern(
        string value,
        SmartStructureRoutingMatchMode matchMode,
        string pattern)
    {
        try
        {
            return matchMode switch
            {
                SmartStructureRoutingMatchMode.Equals => string.Equals(value.Trim(), pattern.Trim(), StringComparison.OrdinalIgnoreCase),
                SmartStructureRoutingMatchMode.Regex => Regex.IsMatch(
                    value,
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    RegexMatchTimeout),
                _ => value.Contains(pattern, StringComparison.OrdinalIgnoreCase)
            };
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string InferStructuralTableKind(
        SmartConfigurationRecognizedTable table,
        double mappedFieldScore)
    {
        return table.SpecificationColumnIndex.HasValue && mappedFieldScore >= 0.75
            ? "AcceptanceSpec"
            : "Unknown";
    }

    private static bool HasStructureHeaderSignal(SmartConfigurationRecognizedTable table)
    {
        return table.Headers.Any(header =>
        {
            var normalized = header.Trim().ToLowerInvariant();
            return normalized.Contains("项目") ||
                   normalized.Contains("項目") ||
                   normalized.Contains("规格") ||
                   normalized.Contains("規格") ||
                   normalized.Contains("标准") ||
                   normalized.Contains("標準") ||
                   normalized.Contains("要求") ||
                   normalized.Contains("验收") ||
                   normalized.Contains("驗收") ||
                   normalized.Contains("判定") ||
                   normalized.Contains("检查") ||
                   normalized.Contains("檢查") ||
                   normalized.Contains("检验") ||
                   normalized.Contains("檢驗") ||
                   normalized.Contains("测试") ||
                   normalized.Contains("測試") ||
                   normalized.Contains("spec") ||
                   normalized.Contains("standard") ||
                   normalized.Contains("acceptance") ||
                   normalized.Contains("inspection");
        });
    }

    private static string GetDefaultRecommendation(
        SmartConfigurationRecognizedTable table,
        DocumentStructureHealthCheckResult? healthCheck,
        double mappedFieldScore,
        double kindScore)
    {
        if (table.Decision == "AutoApply" && (healthCheck == null || healthCheck.CanAutoApply))
        {
            return "Recommended";
        }

        return mappedFieldScore >= 0.75 && kindScore >= 0.65
            ? "NeedConfirm"
            : "Optional";
    }

    private static SmartConfigurationRecognizedTable CopyWithRouting(
        SmartConfigurationRecognizedTable table,
        SmartConfigurationTableRoutingDecision route)
    {
        return new SmartConfigurationRecognizedTable
        {
            TableIndex = table.TableIndex,
            TableName = table.TableName,
            Headers = table.Headers,
            HeaderRowIndex = table.HeaderRowIndex,
            HeaderRowCount = table.HeaderRowCount,
            DataStartRowIndex = table.DataStartRowIndex,
            DataEndRowIndex = table.DataEndRowIndex,
            ProjectColumnIndex = table.ProjectColumnIndex,
            SpecificationColumnIndex = table.SpecificationColumnIndex,
            AcceptanceColumnIndex = table.AcceptanceColumnIndex,
            RemarkColumnIndex = table.RemarkColumnIndex,
            IsSpecificationOnly = table.IsSpecificationOnly,
            Confidence = table.Confidence,
            Source = table.Source,
            Decision = table.Decision,
            TableKind = route.TableKind,
            Recommendation = route.Recommendation,
            RankingScore = route.RankingScore,
            SkipReason = route.SkipReason,
            Issues = route.Issues.ToList(),
            Fields = table.Fields
        };
    }

    private static double CalculateMappedFieldScore(SmartConfigurationRecognizedTable table)
    {
        var requiredCount = table.IsSpecificationOnly ? 3 : 4;
        var mappedCount = 0;
        if (table.IsSpecificationOnly || table.ProjectColumnIndex.HasValue)
        {
            mappedCount++;
        }

        if (table.SpecificationColumnIndex.HasValue)
        {
            mappedCount++;
        }

        if (table.AcceptanceColumnIndex.HasValue)
        {
            mappedCount++;
        }

        if (table.RemarkColumnIndex.HasValue)
        {
            mappedCount++;
        }

        return (double)mappedCount / requiredCount;
    }

    private static double GetKindScore(string kind, SmartStructureRoutingRule? matchedRule)
    {
        if (matchedRule != null)
        {
            return Math.Clamp(matchedRule.Weight, 0, 1);
        }

        return kind switch
        {
            "AcceptanceSpec" => 1.0,
            "Unknown" => 0.45,
            _ => 0.65
        };
    }

    private static List<SmartConfigurationRecognitionIssue> BuildHealthIssues(
        DocumentStructureHealthCheckResult? healthCheck)
    {
        if (healthCheck == null)
        {
            return [];
        }

        return healthCheck.Issues
            .Select(issue => new SmartConfigurationRecognitionIssue
            {
                Code = issue.Code.ToString(),
                Severity = issue.Code == DocumentStructureHealthIssueCode.LowConfidence
                    ? "Info"
                    : "Warning",
                Field = GuessField(issue.Code),
                Message = issue.Message
            })
            .ToList();
    }

    private static string? GuessField(DocumentStructureHealthIssueCode code) => code switch
    {
        DocumentStructureHealthIssueCode.MissingSpecificationColumn => "Specification",
        DocumentStructureHealthIssueCode.EmptySpecificationDataArea => "Specification",
        DocumentStructureHealthIssueCode.MissingProjectColumn => "Project",
        DocumentStructureHealthIssueCode.MissingAcceptanceColumn => "Acceptance",
        DocumentStructureHealthIssueCode.MissingRemarkColumn => "Remark",
        DocumentStructureHealthIssueCode.AmbiguousColumnMapping => null,
        _ => null
    };
}

internal sealed record SmartConfigurationTableRoutingDecision(
    string TableKind,
    string Recommendation,
    double RankingScore,
    string? SkipReason,
    IReadOnlyList<SmartConfigurationRecognitionIssue> Issues);
