using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore;

// =====================================================================================
// SmartFillInsightReport —— 智能填充实情统计（只读分析工具）
//
// 用途：从执行历史(ExecutionHistoryRecords, TaskType=smart-fill)的回放归档中，
//      统计「确定性直达 vs AI 语义 vs 人工」占比、灰区/人工确认率、置信度分布、
//      AI 等价裁决结论分布、硬冲突分布，以及「未识别单位 / 未识别品牌」Top 清单。
//      用于回答“客户能看到的 AI 效果到什么程度”以及“最该补哪些品牌/单位”。
//
// 只读：仅 SELECT，不写库、不改任何业务数据。运行由使用者发起。
//
// 用法：
//   dotnet run --project tools/SmartFillInsightReport -- \
//     --connection "Server=localhost;Database=...;User=root;Password=...;CharSet=utf8mb4;" \
//     [--top 20] [--task-type smart-fill] [--from 2026-01-01] [--to 2026-12-31] [--output report.json]
// =====================================================================================

var connection = GetArg(args, "--connection");
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine(
        """
        缺少必填参数 --connection。

        示例（连接串格式可参考 appsettings.Development.json 的 ConnectionStrings:DefaultConnection）：
          dotnet run --project tools/SmartFillInsightReport -- \
            --connection "Server=localhost;Database=acceptance_spec_db;User=root;Password=***;CharSet=utf8mb4;" \
            --top 20 --output smart-fill-insight.json

        可选参数：
          --top <N>          未识别单位/品牌 Top 清单条数（默认 20）
          --task-type <类型> 默认 smart-fill
          --from <yyyy-MM-dd> 起始时间（UTC，含）
          --to   <yyyy-MM-dd> 结束时间（UTC，含）
          --output <路径>    额外输出 JSON 报告
        """);
    return 2;
}

var topN = int.TryParse(GetArg(args, "--top"), out var parsedTop) && parsedTop > 0 ? parsedTop : 20;
var taskType = GetArg(args, "--task-type") ?? "smart-fill";
var from = ParseUtcDate(GetArg(args, "--from"));
var to = ParseUtcDate(GetArg(args, "--to"));
var outputPath = GetArg(args, "--output");

var webJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

List<RecordRow> rawRecords;
try
{
    await using var db = new AppDbContext(connection);
    var query = db.ExecutionHistoryRecords
        .AsNoTracking()
        .Where(record => record.TaskType == taskType && record.DetailJson != string.Empty);

    if (from.HasValue)
    {
        query = query.Where(record => record.CreatedAt >= from.Value);
    }

    if (to.HasValue)
    {
        query = query.Where(record => record.CreatedAt <= to.Value);
    }

    rawRecords = await query
        .OrderBy(record => record.CreatedAt)
        .Select(record => new RecordRow(
            record.Id,
            record.TaskId,
            record.CreatedAt,
            record.DetailJson,
            record.TotalRowCount,
            record.MatchedRowCount,
            record.AdoptedRowCount,
            record.UnmatchedRowCount,
            record.NotAdoptedRowCount,
            record.ManualSelectedRowCount))
        .ToListAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"连接或查询数据库失败：{ex.Message}");
    Console.Error.WriteLine("请检查连接串、数据库是否可达、ExecutionHistoryRecords 表是否存在。");
    return 3;
}

var report = new InsightReport
{
    TaskType = taskType,
    PeriodFrom = from,
    PeriodTo = to,
    RecordTotal = rawRecords.Count
};

foreach (var record in rawRecords)
{
    DetailDto? detail;
    try
    {
        detail = JsonSerializer.Deserialize<DetailDto>(record.DetailJson, webJson);
    }
    catch (Exception ex)
    {
        report.ParseFailures++;
        Console.Error.WriteLine($"记录 {record.Id}({record.TaskId}) DetailJson 解析失败：{ex.Message}");
        continue;
    }

    if (detail == null)
    {
        report.ParseFailures++;
        continue;
    }

    // 任务级汇总（即使没有回放也可用）
    report.RecordLevelTotalRows += record.TotalRowCount;
    report.RecordLevelAdoptedRows += record.AdoptedRowCount;
    report.RecordLevelUnmatchedRows += record.UnmatchedRowCount;

    if (detail.SmartFillSummary is { } summary)
    {
        report.SummaryExactRows += summary.ExactMatchedRowCount ?? 0;
        report.SummaryAiRows += summary.AiMatchedRowCount ?? 0;
        report.SummaryManualConfirmedRows += summary.ManualConfirmedRowCount ?? 0;
        report.SummaryManualEditedRows += summary.ManualEditedRowCount ?? 0;
    }

    var playback = detail.SmartFillPlayback;
    var rows = playback?.Files?
        .SelectMany(file => file.Sheets ?? [])
        .SelectMany(sheet => sheet.Rows ?? [])
        .ToList();

    if (playback == null || playback.IsLegacy || rows == null || rows.Count == 0)
    {
        report.RecordsWithoutPlayback++;
        continue;
    }

    report.RecordsWithPlayback++;

    foreach (var row in rows)
    {
        report.RowsAnalyzed++;

        Bump(report.MatchOrigin, string.IsNullOrWhiteSpace(row.MatchOrigin) ? "none" : row.MatchOrigin);
        Bump(report.Status, string.IsNullOrWhiteSpace(row.Status) ? "(空)" : row.Status);
        if (row.IsManualConfirmed) report.ManualConfirmedRows++;
        if (row.IsManualEdited) report.ManualEditedRows++;

        var snapshot = row.PreviewSnapshot;
        Bump(report.ConfidenceLevel, string.IsNullOrWhiteSpace(snapshot?.ConfidenceLevel) ? "none" : snapshot!.ConfidenceLevel!);

        var best = snapshot?.BestMatch;
        if (best == null)
        {
            continue;
        }

        Bump(report.SelectionMode, string.IsNullOrWhiteSpace(best.SelectionMode) ? "(空)" : best.SelectionMode!);
        Bump(report.Decision, string.IsNullOrWhiteSpace(best.Decision) ? "(空)" : best.Decision!);
        if (best.IsAmbiguous) report.AmbiguousRows++;

        if (best.LlmEquivalence is { } llm)
        {
            report.RowsWithEquivalenceVerdict++;
            Bump(report.EquivalenceVerdict, string.IsNullOrWhiteSpace(llm.Verdict) ? "(空)" : llm.Verdict!);
            Bump(report.EquivalenceReasonType, string.IsNullOrWhiteSpace(llm.ReasonType) ? "(空)" : llm.ReasonType!);
            report.EquivalenceConfidenceSum += llm.Confidence;
        }

        foreach (var issue in best.Issues ?? [])
        {
            if (string.IsNullOrWhiteSpace(issue.Code))
            {
                continue;
            }

            Bump(report.IssueCode, issue.Code);

            if (string.Equals(issue.Severity, "hard_conflict", StringComparison.OrdinalIgnoreCase))
            {
                Bump(report.HardConflictCode, issue.Code);
            }

            if (string.Equals(issue.Code, "unknown_unit_token", StringComparison.OrdinalIgnoreCase))
            {
                CollectTokens(report.UnknownUnitTokens, issue.SourceValue, issue.CandidateValue);
            }
            else if (string.Equals(issue.Code, "unknown_brand_token", StringComparison.OrdinalIgnoreCase))
            {
                CollectTokens(report.UnknownBrandTokens, issue.SourceValue, issue.CandidateValue);
            }
        }
    }
}

PrintReport(report, topN);

if (!string.IsNullOrWhiteSpace(outputPath))
{
    var resolved = Path.GetFullPath(outputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(resolved) ?? ".");
    var payload = report.ToOutputPayload(topN);
    await File.WriteAllTextAsync(
        resolved,
        JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine();
    Console.WriteLine($"JSON 报告已写入：{resolved}");
}

return 0;

// ============================ 辅助函数 ============================

static string? GetArg(string[] args, string name)
{
    var index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static DateTime? ParseUtcDate(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    if (DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
    {
        return parsed;
    }

    Console.Error.WriteLine($"无法解析时间参数：{value}（已忽略）");
    return null;
}

static void Bump(Dictionary<string, int> counter, string key)
{
    counter[key] = counter.GetValueOrDefault(key) + 1;
}

// 未识别单位/品牌的 sourceValue/candidateValue 是「、」连接的 token 列表，拆开逐个计数
static void CollectTokens(Dictionary<string, int> counter, string? sourceValue, string? candidateValue)
{
    foreach (var raw in new[] { sourceValue, candidateValue })
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            continue;
        }

        foreach (var token in raw.Split(['、', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token is "无" or "-")
            {
                continue;
            }

            Bump(counter, token);
        }
    }
}

static string Pct(int part, int whole)
{
    return whole <= 0 ? "0.0%" : (100.0 * part / whole).ToString("F1", CultureInfo.InvariantCulture) + "%";
}

static void PrintCounter(
    string title,
    Dictionary<string, int> counter,
    int denominator,
    IReadOnlyDictionary<string, string> labels)
{
    Console.WriteLine();
    Console.WriteLine(title);
    if (counter.Count == 0)
    {
        Console.WriteLine("  （无数据）");
        return;
    }

    foreach (var (key, count) in counter.OrderByDescending(kv => kv.Value))
    {
        var label = labels.GetValueOrDefault(key, key);
        Console.WriteLine($"  {label,-20} {count,8}   {Pct(count, denominator)}");
    }
}

static void PrintTokenTop(string title, Dictionary<string, int> counter, int topN)
{
    Console.WriteLine();
    Console.WriteLine(title);
    if (counter.Count == 0)
    {
        Console.WriteLine("  （无，说明覆盖良好或样本中未出现未识别项）");
        return;
    }

    var rank = 0;
    foreach (var (token, count) in counter.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(topN))
    {
        rank++;
        Console.WriteLine($"  {rank,3}. {token,-24} 出现 {count} 次");
    }
    Console.WriteLine($"  （共 {counter.Count} 种不同 token，上面取 Top {Math.Min(topN, counter.Count)}）");
}

static void PrintReport(InsightReport report, int topN)
{
    var sb = new StringBuilder();
    sb.AppendLine("==================== 智能填充实情统计 ====================");
    sb.Append("任务类型：").Append(report.TaskType);
    if (report.PeriodFrom.HasValue || report.PeriodTo.HasValue)
    {
        sb.Append("   时间范围(UTC)：")
          .Append(report.PeriodFrom?.ToString("yyyy-MM-dd") ?? "不限")
          .Append(" ~ ")
          .Append(report.PeriodTo?.ToString("yyyy-MM-dd") ?? "不限");
    }
    Console.WriteLine(sb.ToString());

    Console.WriteLine();
    Console.WriteLine("【数据概况】");
    Console.WriteLine($"  执行记录总数            {report.RecordTotal}");
    Console.WriteLine($"  含行级回放归档的记录    {report.RecordsWithPlayback}");
    Console.WriteLine($"  无回放(旧/压缩)的记录   {report.RecordsWithoutPlayback}");
    Console.WriteLine($"  解析失败的记录          {report.ParseFailures}");
    Console.WriteLine($"  可做行级分析的行数      {report.RowsAnalyzed}");
    Console.WriteLine($"  任务级总行数(含无回放)  {report.RecordLevelTotalRows}（已采用 {report.RecordLevelAdoptedRows} / 未匹配 {report.RecordLevelUnmatchedRows}）");

    if (report.RowsAnalyzed == 0)
    {
        Console.WriteLine();
        Console.WriteLine("没有可做行级分析的回放数据。可能原因：执行历史为空，或都是无回放归档的旧记录。");
        if (report.SummaryExactRows + report.SummaryAiRows > 0)
        {
            Console.WriteLine();
            Console.WriteLine("【退化汇总（来自 SmartFillSummary，可能不完整）】");
            Console.WriteLine($"  精确/规范化直达行   {report.SummaryExactRows}");
            Console.WriteLine($"  AI 语义匹配行       {report.SummaryAiRows}");
            Console.WriteLine($"  人工确认行          {report.SummaryManualConfirmedRows}");
            Console.WriteLine($"  人工改写行          {report.SummaryManualEditedRows}");
        }
        return;
    }

    var denom = report.RowsAnalyzed;

    PrintCounter("【命中来源构成】(确定性直达 vs AI 语义 vs 无匹配)", report.MatchOrigin, denom, Labels.MatchOrigin);
    PrintCounter("【最终决策构成】(人工确认率 = 人工确认占比)", report.Decision, denom, Labels.Decision);
    PrintCounter("【选定方式】(aiRerank = LLM 实际参与改选)", report.SelectionMode, denom, Labels.SelectionMode);
    PrintCounter("【填充状态】", report.Status, denom, Labels.Status);

    Console.WriteLine();
    Console.WriteLine("【人工介入】");
    Console.WriteLine($"  人工确认行   {report.ManualConfirmedRows,8}   {Pct(report.ManualConfirmedRows, denom)}");
    Console.WriteLine($"  人工改写行   {report.ManualEditedRows,8}   {Pct(report.ManualEditedRows, denom)}   ← 可回写规格库的纠错信号");
    Console.WriteLine($"  高歧义行     {report.AmbiguousRows,8}   {Pct(report.AmbiguousRows, denom)}");

    PrintCounter("【置信度分布】", report.ConfidenceLevel, denom, Labels.ConfidenceLevel);

    Console.WriteLine();
    Console.WriteLine("【AI 等价裁决（灰区）】");
    Console.WriteLine($"  带裁决结论的行   {report.RowsWithEquivalenceVerdict,8}   {Pct(report.RowsWithEquivalenceVerdict, denom)}");
    if (report.RowsWithEquivalenceVerdict > 0)
    {
        Console.WriteLine($"  平均自评置信度   {(report.EquivalenceConfidenceSum / report.RowsWithEquivalenceVerdict):F3}");
    }
    PrintCounter("  裁决结论分布", report.EquivalenceVerdict, report.RowsWithEquivalenceVerdict, Labels.Verdict);
    PrintCounter("  原因类型分布", report.EquivalenceReasonType, report.RowsWithEquivalenceVerdict, Labels.ReasonType);
    Console.WriteLine("  注：verdict=等价 含「确定性快路径合成的等价」，并非全部来自 LLM 真实调用；");
    Console.WriteLine("      不同/不确定 基本可视为 LLM 真实介入。真实灰区上界 ≈ 不同 + 不确定 + 人工确认。");

    PrintCounter("【硬冲突分布】(强制人工的结构化冲突)", report.HardConflictCode, denom, Labels.IssueCode);
    PrintCounter("【全部问题码分布】", report.IssueCode, denom, Labels.IssueCode);

    PrintTokenTop($"【Top {topN} 未识别单位】(unknown_unit_token —— 建议补进 smart-fill-knowledge.json)", report.UnknownUnitTokens, topN);
    PrintTokenTop($"【Top {topN} 未识别品牌】(unknown_brand_token —— 建议补进品牌字典)", report.UnknownBrandTokens, topN);

    Console.WriteLine();
    Console.WriteLine("==========================================================");
}

// ============================ 数据结构 ============================

internal sealed record RecordRow(
    int Id,
    string TaskId,
    DateTime CreatedAt,
    string DetailJson,
    int TotalRowCount,
    int MatchedRowCount,
    int AdoptedRowCount,
    int UnmatchedRowCount,
    int NotAdoptedRowCount,
    int ManualSelectedRowCount);

// —— DetailJson 的最小镜像（仅声明用到的字段；camelCase 由 Web 选项自动匹配）——
internal sealed class DetailDto
{
    public string? TaskType { get; set; }
    public SummaryDto? SmartFillSummary { get; set; }
    public PlaybackDto? SmartFillPlayback { get; set; }
}

internal sealed class SummaryDto
{
    public int? ExactMatchedRowCount { get; set; }
    public int? AiMatchedRowCount { get; set; }
    public int? ManualConfirmedRowCount { get; set; }
    public int? ManualEditedRowCount { get; set; }
    public int? NotUsedRowCount { get; set; }
}

internal sealed class PlaybackDto
{
    public bool IsLegacy { get; set; }
    public List<PlaybackFileDto>? Files { get; set; }
}

internal sealed class PlaybackFileDto
{
    public List<PlaybackSheetDto>? Sheets { get; set; }
}

internal sealed class PlaybackSheetDto
{
    public List<PlaybackRowDto>? Rows { get; set; }
}

internal sealed class PlaybackRowDto
{
    public string? Status { get; set; }
    public string? MatchOrigin { get; set; }
    public bool IsManualConfirmed { get; set; }
    public bool IsManualEdited { get; set; }
    public PreviewSnapshotDto? PreviewSnapshot { get; set; }
}

internal sealed class PreviewSnapshotDto
{
    public string? ConfidenceLevel { get; set; }
    public BestMatchDto? BestMatch { get; set; }
}

internal sealed class BestMatchDto
{
    public string? Decision { get; set; }
    public string? SelectionMode { get; set; }
    public bool IsAmbiguous { get; set; }
    public List<IssueDto>? Issues { get; set; }
    public EquivalenceDto? LlmEquivalence { get; set; }
}

internal sealed class IssueDto
{
    public string? Code { get; set; }
    public string? Severity { get; set; }
    public string? FieldName { get; set; }
    public string? SourceValue { get; set; }
    public string? CandidateValue { get; set; }
}

internal sealed class EquivalenceDto
{
    public string? Verdict { get; set; }
    public string? ReasonType { get; set; }
    public double Confidence { get; set; }
}

// —— 聚合结果 ——
internal sealed class InsightReport
{
    public string TaskType { get; set; } = "smart-fill";
    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }

    public int RecordTotal { get; set; }
    public int RecordsWithPlayback { get; set; }
    public int RecordsWithoutPlayback { get; set; }
    public int ParseFailures { get; set; }
    public int RowsAnalyzed { get; set; }

    public int RecordLevelTotalRows { get; set; }
    public int RecordLevelAdoptedRows { get; set; }
    public int RecordLevelUnmatchedRows { get; set; }

    public int SummaryExactRows { get; set; }
    public int SummaryAiRows { get; set; }
    public int SummaryManualConfirmedRows { get; set; }
    public int SummaryManualEditedRows { get; set; }

    public int ManualConfirmedRows { get; set; }
    public int ManualEditedRows { get; set; }
    public int AmbiguousRows { get; set; }
    public int RowsWithEquivalenceVerdict { get; set; }
    public double EquivalenceConfidenceSum { get; set; }

    public Dictionary<string, int> MatchOrigin { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> Decision { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> SelectionMode { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> Status { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> ConfidenceLevel { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> EquivalenceVerdict { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> EquivalenceReasonType { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> IssueCode { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> HardConflictCode { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> UnknownUnitTokens { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> UnknownBrandTokens { get; } = new(StringComparer.Ordinal);

    public object ToOutputPayload(int topN) => new
    {
        taskType = TaskType,
        periodFrom = PeriodFrom,
        periodTo = PeriodTo,
        recordTotal = RecordTotal,
        recordsWithPlayback = RecordsWithPlayback,
        recordsWithoutPlayback = RecordsWithoutPlayback,
        parseFailures = ParseFailures,
        rowsAnalyzed = RowsAnalyzed,
        recordLevelTotalRows = RecordLevelTotalRows,
        recordLevelAdoptedRows = RecordLevelAdoptedRows,
        recordLevelUnmatchedRows = RecordLevelUnmatchedRows,
        manualConfirmedRows = ManualConfirmedRows,
        manualEditedRows = ManualEditedRows,
        ambiguousRows = AmbiguousRows,
        rowsWithEquivalenceVerdict = RowsWithEquivalenceVerdict,
        avgEquivalenceConfidence = RowsWithEquivalenceVerdict > 0
            ? EquivalenceConfidenceSum / RowsWithEquivalenceVerdict
            : 0,
        matchOrigin = MatchOrigin,
        decision = Decision,
        selectionMode = SelectionMode,
        status = Status,
        confidenceLevel = ConfidenceLevel,
        equivalenceVerdict = EquivalenceVerdict,
        equivalenceReasonType = EquivalenceReasonType,
        issueCode = IssueCode,
        hardConflictCode = HardConflictCode,
        topUnknownUnits = UnknownUnitTokens.OrderByDescending(kv => kv.Value).Take(topN)
            .Select(kv => new { token = kv.Key, count = kv.Value }),
        topUnknownBrands = UnknownBrandTokens.OrderByDescending(kv => kv.Value).Take(topN)
            .Select(kv => new { token = kv.Key, count = kv.Value })
    };
}

// —— 中文标签映射（仅用于展示）——
internal static class Labels
{
    public static readonly IReadOnlyDictionary<string, string> MatchOrigin = new Dictionary<string, string>
    {
        ["exact"] = "完全/规范化直达",
        ["ai"] = "AI 语义匹配",
        ["none"] = "无匹配"
    };

    public static readonly IReadOnlyDictionary<string, string> Decision = new Dictionary<string, string>
    {
        ["autoApply"] = "自动填充",
        ["manualReview"] = "人工确认",
        ["reject"] = "不建议填充"
    };

    public static readonly IReadOnlyDictionary<string, string> SelectionMode = new Dictionary<string, string>
    {
        ["exactShortcut"] = "精确直达",
        ["embeddingTop1"] = "Embedding 直选",
        ["aiRerank"] = "AI 改选"
    };

    public static readonly IReadOnlyDictionary<string, string> Status = new Dictionary<string, string>
    {
        ["adopted"] = "已采用",
        ["not-adopted"] = "未采用",
        ["unmatched"] = "无匹配",
        ["skipped"] = "已跳过"
    };

    public static readonly IReadOnlyDictionary<string, string> ConfidenceLevel = new Dictionary<string, string>
    {
        ["high"] = "高",
        ["medium"] = "中",
        ["low"] = "低",
        ["none"] = "无"
    };

    public static readonly IReadOnlyDictionary<string, string> Verdict = new Dictionary<string, string>
    {
        ["equivalent"] = "等价",
        ["different"] = "不同",
        ["uncertain"] = "不确定"
    };

    public static readonly IReadOnlyDictionary<string, string> ReasonType = new Dictionary<string, string>
    {
        ["format_only"] = "格式差异",
        ["punctuation_only"] = "标点差异",
        ["equivalent_expression"] = "等价表达",
        ["symbol_equivalent"] = "符号等价",
        ["semantic_difference"] = "语义差异",
        ["symbol_conflict"] = "符号冲突",
        ["uncertain"] = "无法确认"
    };

    public static readonly IReadOnlyDictionary<string, string> IssueCode = new Dictionary<string, string>
    {
        ["numeric_unit_conflict"] = "数值/单位冲突",
        ["cross_temperature_scale"] = "跨温标",
        ["dimension_tuple_conflict"] = "尺寸元组冲突",
        ["comparator_conflict"] = "比较符方向冲突",
        ["polarity_conflict"] = "方向/极性冲突",
        ["negative_prefix_conflict"] = "否定前缀冲突",
        ["unknown_unit_token"] = "未识别单位",
        ["unknown_brand_token"] = "未识别品牌",
        ["unsupported_format_token"] = "未覆盖格式",
        ["identifier_conflict"] = "型号/料号冲突"
    };
}
