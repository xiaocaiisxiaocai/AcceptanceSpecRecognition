using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Documents.Models;

namespace AcceptanceSpecSystem.Application.Services;

internal static class SmartConfigurationTableRoutingService
{
    private static readonly string[] AcceptanceSignals =
    [
        "验收", "驗收", "规格", "規格", "技术要求", "技術要求", "验收标准", "驗收標準",
        "供方能力", "厂商确认", "廠商確認", "acceptance", "spec"
    ];

    private static readonly string[] SafetySignals =
    [
        "安全", "工安", "安全规范", "安全規範", "safety"
    ];

    private static readonly string[] EnvironmentalSignals =
    [
        "环保", "環保", "排废", "排廢", "废气", "廢氣", "environment"
    ];

    private static readonly string[] SecsSignals =
    [
        "secs", "gem", "通讯", "通訊"
    ];

    private static readonly string[] UtilitySignals =
    [
        "utility", "水电", "水電", "电力需求", "電力需求", "冰水", "空压", "空壓", "排气", "排氣"
    ];

    private static readonly string[] QuotationSignals =
    [
        "报价", "報價", "quotation", "quote", "单价", "單價", "金额", "金額"
    ];

    private static readonly string[] LayoutSignals =
    [
        "layout", "布局", "平面图", "平面圖", "设备位置", "設備位置", "x", "y"
    ];

    private static readonly string[] BomSignals =
    [
        "备品", "備品", "赠品", "贈品", "bom", "配件名称", "配件名稱", "品牌", "规格型号", "規格型號", "数量", "數量"
    ];

    private static readonly string[] SignatureSignals =
    [
        "签核", "簽核", "会签", "會簽", "封面", "技术小组", "技術小組", "核准", "批准"
    ];

    public static SmartConfigurationRecognizedTable Enrich(
        TableInfo? tableInfo,
        TableData tableData,
        SmartConfigurationRecognizedTable table,
        DocumentStructureHealthCheckResult? healthCheck,
        double referenceCaseScore = 0)
    {
        var route = Route(tableInfo, tableData, table, healthCheck, referenceCaseScore);
        return CopyWithRouting(table, route);
    }

    public static SmartConfigurationTableRoutingDecision Route(
        TableInfo? tableInfo,
        TableData tableData,
        SmartConfigurationRecognizedTable table,
        DocumentStructureHealthCheckResult? healthCheck,
        double referenceCaseScore = 0)
    {
        var text = BuildSearchText(tableInfo, tableData, table);
        var kind = InferTableKind(text);
        var mappedFieldScore = CalculateMappedFieldScore(table);
        var healthIssues = BuildHealthIssues(healthCheck);
        var issues = new List<SmartConfigurationRecognitionIssue>(healthIssues);

        var kindScore = GetKindScore(kind);
        var clampedReferenceScore = Math.Clamp(referenceCaseScore, 0, 1);
        var rankingScore = Math.Clamp(
            table.Confidence * 0.35 +
            mappedFieldScore * 0.30 +
            kindScore * 0.25 +
            clampedReferenceScore * 0.10,
            0,
            1);

        var skipReason = GetSkipReason(kind);
        if (skipReason != null)
        {
            rankingScore = Math.Min(rankingScore, 0.35);
            issues.Insert(0, new SmartConfigurationRecognitionIssue
            {
                Code = $"TableKind.{kind}",
                Severity = "Info",
                Message = skipReason
            });

            return new SmartConfigurationTableRoutingDecision(
                kind,
                "Skip",
                Math.Round(rankingScore, 2),
                skipReason,
                issues);
        }

        var recommendation = table.Decision == "AutoApply" && (healthCheck == null || healthCheck.CanAutoApply)
            ? "Recommended"
            : mappedFieldScore >= 0.75 && kindScore >= 0.65
                ? "NeedConfirm"
                : "Optional";

        if (healthIssues.Count > 0 && recommendation == "Recommended")
        {
            recommendation = "NeedConfirm";
        }

        return new SmartConfigurationTableRoutingDecision(
            kind,
            recommendation,
            Math.Round(rankingScore, 2),
            null,
            issues);
    }

    public static bool ShouldSkipStructureAdjudication(
        TableInfo? tableInfo,
        TableData tableData,
        SmartConfigurationRecognizedTable table)
    {
        var route = Route(tableInfo, tableData, table, healthCheck: null);
        return route.Recommendation == "Skip";
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

    private static string InferTableKind(string text)
    {
        if (ContainsAny(text, QuotationSignals))
        {
            return "Quotation";
        }

        if (ContainsAny(text, LayoutSignals) && !ContainsAny(text, AcceptanceSignals))
        {
            return "Layout";
        }

        if (ContainsAny(text, UtilitySignals))
        {
            return "Utility";
        }

        if (ContainsAny(text, BomSignals))
        {
            return "BomOrSpareParts";
        }

        if (ContainsAny(text, SignatureSignals) && !ContainsAny(text, AcceptanceSignals))
        {
            return "SignatureOrCover";
        }

        if (ContainsAny(text, SecsSignals) && ContainsAny(text, AcceptanceSignals))
        {
            return "SecsSpec";
        }

        if (ContainsAny(text, SafetySignals) && ContainsAny(text, AcceptanceSignals))
        {
            return "SafetySpec";
        }

        if (ContainsAny(text, EnvironmentalSignals) && ContainsAny(text, AcceptanceSignals))
        {
            return "EnvironmentalSpec";
        }

        return ContainsAny(text, AcceptanceSignals)
            ? "AcceptanceSpec"
            : "Unknown";
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

    private static double GetKindScore(string kind) => kind switch
    {
        "AcceptanceSpec" => 1.0,
        "SafetySpec" or "EnvironmentalSpec" or "SecsSpec" => 0.85,
        "Unknown" => 0.45,
        _ => 0.05
    };

    private static string? GetSkipReason(string kind) => kind switch
    {
        "Quotation" => "该表疑似报价单，不属于验收规格主表，建议跳过。",
        "Layout" => "该表疑似 Layout 或设备布局信息，不属于验收规格主表，建议跳过。",
        "Utility" => "该表疑似水电气等 Utility 需求，不属于验收规格主表，建议跳过。",
        "BomOrSpareParts" => "该表疑似备品、赠品或配件清单，不属于验收规格主表，建议跳过。",
        "SignatureOrCover" => "该表疑似封面、签核或会签信息，不属于验收规格主表，建议跳过。",
        _ => null
    };

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
        _ => null
    };

    private static string BuildSearchText(
        TableInfo? tableInfo,
        TableData tableData,
        SmartConfigurationRecognizedTable table)
    {
        var rowValues = tableData.Rows
            .Take(3)
            .SelectMany(row => row.Cells)
            .Select(cell => cell.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return string.Join(
            " ",
            new[] { tableInfo?.Name, table.TableName }
                .Concat(table.Headers)
                .Concat(rowValues)
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static bool ContainsAny(string text, IReadOnlyList<string> signals)
    {
        return signals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed record SmartConfigurationTableRoutingDecision(
    string TableKind,
    string Recommendation,
    double RankingScore,
    string? SkipReason,
    IReadOnlyList<SmartConfigurationRecognitionIssue> Issues);
