using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

// =====================================================================================
// SmartStructureHeaderGapReport —— 智能结构识别表头规则缺口统计（只读分析工具）
//
// 用途：从 DocumentTemplates 的“用户确认后最终列映射”反查当前 ColumnMappingRules
//      是否覆盖这些表头，输出全局规则未覆盖与当前客户有效规则未覆盖 TopN。
//
// 只读：仅 SELECT，不写库、不改任何业务数据。
//
// 用法：
//   dotnet run --project tools/SmartStructureHeaderGapReport -- \
//     [--connection "Server=localhost;Database=...;User=root;Password=...;CharSet=utf8mb4;"] \
//     [--samples samples.json] [--top 20] [--from 2026-01-01] [--to 2026-12-31] [--output report.json]
// =====================================================================================

var connection = GetArg(args, "--connection");
var samplesPath = GetArg(args, "--samples");
if (string.IsNullOrWhiteSpace(connection) && string.IsNullOrWhiteSpace(samplesPath))
{
    Console.Error.WriteLine(
        """
        缺少输入参数：--connection 与 --samples 至少提供一个。

        示例：
          dotnet run --project tools/SmartStructureHeaderGapReport -- \
            --connection "Server=localhost;Database=acceptance_spec_db;User=root;Password=***;CharSet=utf8mb4;" \
            --top 20 --output smart-structure-header-gap.json

          dotnet run --project tools/SmartStructureHeaderGapReport -- \
            --samples samples.json --top 20

        可选参数：
          --connection <连接串> 读取 DocumentTemplates / ColumnMappingRules
          --samples <路径>      额外读取离线样本 JSON；无连接串时使用内置默认规则
          --top <N>           Top 清单条数（默认 20）
          --from <yyyy-MM-dd> 模板确认/更新时间起始时间（UTC，含）
          --to   <yyyy-MM-dd> 模板确认/更新时间结束时间（UTC，含）
          --output <路径>     额外输出 JSON 报告
        """);
    return 2;
}

var topN = int.TryParse(GetArg(args, "--top"), out var parsedTop) && parsedTop > 0 ? parsedTop : 20;
var from = ParseUtcDate(GetArg(args, "--from"));
var to = ParseUtcDate(GetArg(args, "--to"));
var outputPath = GetArg(args, "--output");

SmartStructureHeaderGapReport report;
try
{
    var templates = new List<DocumentTemplate>();
    var rules = new List<ColumnMappingRule>();

    if (!string.IsNullOrWhiteSpace(connection))
    {
        await using var db = new AppDbContext(connection);
        var templateQuery = db.DocumentTemplates.AsNoTracking();

        if (from.HasValue)
        {
            templateQuery = templateQuery.Where(template =>
                (template.ConfirmedAt ?? template.UpdatedAt) >= from.Value);
        }

        if (to.HasValue)
        {
            templateQuery = templateQuery.Where(template =>
                (template.ConfirmedAt ?? template.UpdatedAt) <= to.Value);
        }

        templates.AddRange(await templateQuery
            .OrderBy(template => template.CustomerId)
            .ThenBy(template => template.Id)
            .ToListAsync());
        rules.AddRange(await db.ColumnMappingRules
            .AsNoTracking()
            .OrderBy(rule => rule.CustomerId)
            .ThenBy(rule => rule.TargetField)
            .ThenByDescending(rule => rule.Priority)
            .ToListAsync());
    }
    else
    {
        rules.AddRange(CreateBuiltinRules());
    }

    if (!string.IsNullOrWhiteSpace(samplesPath))
    {
        templates.AddRange(SmartStructureHeaderGapSampleLoader.LoadFromJson(await File.ReadAllTextAsync(samplesPath)));
    }

    report = SmartStructureHeaderGapAnalyzer.Analyze(templates, rules, topN);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"连接或查询数据库失败：{ex.Message}");
    Console.Error.WriteLine("请检查连接串、数据库是否可达、DocumentTemplates/ColumnMappingRules 表是否存在。");
    return 3;
}

PrintReport(report, topN);

if (!string.IsNullOrWhiteSpace(outputPath))
{
    var resolved = Path.GetFullPath(outputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(resolved) ?? ".");
    await File.WriteAllTextAsync(
        resolved,
        JsonSerializer.Serialize(
            report,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            }));
    Console.WriteLine();
    Console.WriteLine($"JSON 报告已写入：{resolved}");
}

return 0;

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

static void PrintReport(SmartStructureHeaderGapReport report, int topN)
{
    Console.WriteLine("==================== 智能结构识别表头缺口统计 ====================");
    Console.WriteLine($"已确认模板数        {report.TemplateCount}");
    Console.WriteLine($"已映射表头观察数    {report.MappedHeaderObservationCount}");
    PrintConclusion(report.Conclusion);
    PrintItems($"【Top {topN} 全局规则未覆盖】用于判断是否补全局列映射规则", report.GlobalUncoveredHeaders);
    PrintItems($"【Top {topN} 当前有效规则未覆盖】用于判断当前运行时仍会漏的表头", report.EffectiveUncoveredHeaders);
    PrintItems($"【Top {topN} 客户学习规则全局候选】用于判断是否把客户级 Learned 规则晋升/补为全局规则", report.LearnedRuleGlobalCandidates);
    Console.WriteLine("================================================================");
}

static void PrintConclusion(SmartStructureHeaderGapConclusion conclusion)
{
    Console.WriteLine();
    Console.WriteLine("【阶段 1 结论】");
    Console.WriteLine($"  可优先评审的全局规则补齐候选      {conclusion.RuleBackfillCandidateCount}");
    Console.WriteLine($"  客户级规则/继续收样候选           {conclusion.CustomerRuleCandidateCount}");
    Console.WriteLine($"  Learned 规则全局晋升候选          {conclusion.LearnedRulePromotionCandidateCount}");
    Console.WriteLine($"  当前运行时仍可能漏识别的表头种类  {conclusion.EffectiveRuntimeGapCount}");
    Console.WriteLine($"  下一步：{GetNextActionLabel(conclusion.NextAction)}");
    Console.WriteLine("  注：阶段 1 不直接启用 AI 召回；规则补齐后复跑报告仍有稳定缺口，再评估 LLM/Embedding。");
}

static string GetNextActionLabel(SmartStructureHeaderGapNextAction nextAction) => nextAction switch
{
    SmartStructureHeaderGapNextAction.CollectSamples => "当前缺少可统计样本，先导出确认模板或补充离线样本。",
    SmartStructureHeaderGapNextAction.ReviewRuleBackfillFirst => "先评审高频/跨客户表头并补列映射规则，暂不进入 AI 召回。",
    SmartStructureHeaderGapNextAction.ReviewCustomerRulesOrCollectMoreSamples => "先评审客户级规则或继续收样，样本不足时不做全局规则。",
    SmartStructureHeaderGapNextAction.NoAdditionalAction => "当前样本未显示运行时规则缺口，暂不需要新增能力。",
    _ => "继续人工复核报告。"
};

static void PrintItems(string title, IReadOnlyList<SmartStructureHeaderGapItem> items)
{
    Console.WriteLine();
    Console.WriteLine(title);
    if (items.Count == 0)
    {
        Console.WriteLine("  （无数据）");
        return;
    }

    var rank = 0;
    foreach (var item in items)
    {
        rank++;
        Console.WriteLine(
            $"  {rank,3}. {item.Header,-24} -> {item.TargetField,-13} " +
            $"出现 {item.OccurrenceCount,4} 次 / 客户 {item.CustomerCount,3} 个 / " +
            $"客户ID: {string.Join(',', item.CustomerIds.Take(8))}");
        if (item.ExampleTemplateNames.Count > 0)
        {
            Console.WriteLine($"       示例模板：{string.Join("；", item.ExampleTemplateNames)}");
        }
    }
}

static IReadOnlyList<ColumnMappingRule> CreateBuiltinRules()
{
    var rules = new List<ColumnMappingRule>();
    foreach (var (columnType, words) in ColumnMappingRuleDefaults.GetAll())
    {
        var targetField = ToTargetField(columnType);
        if (!targetField.HasValue)
        {
            continue;
        }

        foreach (var word in words)
        {
            rules.Add(new ColumnMappingRule
            {
                CustomerId = null,
                Source = ColumnMappingRuleSource.Builtin,
                TargetField = targetField.Value,
                MatchMode = ColumnMappingMatchMode.Contains,
                Pattern = word,
                Enabled = true
            });
        }
    }

    return rules;
}

static ColumnMappingTargetField? ToTargetField(ColumnType columnType) => columnType switch
{
    ColumnType.Project => ColumnMappingTargetField.Project,
    ColumnType.Specification => ColumnMappingTargetField.Specification,
    ColumnType.Acceptance => ColumnMappingTargetField.Acceptance,
    ColumnType.Remark => ColumnMappingTargetField.Remark,
    _ => null
};
