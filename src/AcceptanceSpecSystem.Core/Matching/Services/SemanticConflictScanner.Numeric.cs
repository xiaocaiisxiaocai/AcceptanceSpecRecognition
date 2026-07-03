using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public sealed partial class SemanticConflictScanner
{
    private void ScanNumericUnitConflicts(MatchEvidence evidence, string srcText, string candText)
    {
        var srcValues = _canonicalizer.ExtractNormalizedValues(srcText);
        var candValues = _canonicalizer.ExtractNormalizedValues(candText);

        if (srcValues.Count == 0 || candValues.Count == 0)
            return;

        // 按量纲分组
        var srcByDim = srcValues.GroupBy(v => v.Dimension)
            .ToDictionary(g => g.Key, g => g.ToList());
        var candByDim = candValues.GroupBy(v => v.Dimension)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 跨温标检测：源与候选使用了不同温标（℃/℉/K），无法自动换算，强制人工
        var srcTempDims = srcByDim.Keys.Where(TemperatureDimensions.Contains).ToList();
        var candTempDims = candByDim.Keys.Where(TemperatureDimensions.Contains).ToList();
        if (srcTempDims.Count > 0 && candTempDims.Count > 0 &&
            !srcTempDims.Intersect(candTempDims).Any())
        {
            var srcExpr = srcByDim[srcTempDims[0]][0].OriginalExpression;
            var candExpr = candByDim[candTempDims[0]][0].OriginalExpression;
            var msg = $"温度跨温标，无法自动比较：{srcExpr} vs {candExpr}";
            evidence.Issues.Add(new MatchIssue
            {
                Code = "cross_temperature_scale",
                Severity = "hard_conflict",
                FieldName = "温度",
                SourceValue = srcExpr,
                CandidateValue = candExpr,
                Message = msg,
                SuggestedAction = "请人工确认温度是否等价"
            });
            evidence.Conflicts.Add(msg);
        }

        // 同量纲数值比较：对每个两边都出现的量纲，比较"排序后的归一值集合"，
        // 避免笛卡尔积把 (220V,24V) vs (220V,24V) 误判为冲突。
        foreach (var (dim, srcList) in srcByDim)
        {
            if (!candByDim.TryGetValue(dim, out var candList))
                continue;

            if (!NumericSetsEqual(srcList, candList, out var srcExpr, out var candExpr))
            {
                var msg = $"数值不等价：{srcExpr} vs {candExpr}（量纲 {dim}）";
                evidence.Issues.Add(new MatchIssue
                {
                    Code = "numeric_unit_conflict",
                    Severity = "hard_conflict",
                    FieldName = "数值/单位",
                    SourceValue = srcExpr,
                    CandidateValue = candExpr,
                    Message = msg,
                    SuggestedAction = "数值或单位不同，请人工确认"
                });
                evidence.Conflicts.Add(msg);
            }
        }
    }

    /// <summary>
    /// 比较两组同量纲归一值是否构成同一集合。
    /// 先折叠各侧容差内的重复值（如同一数值的中英两份表达"30天/30 day"、
    /// 中英对照里重复出现的"10mm"），避免同值多份被当成"数量不等"误判为冲突；
    /// 再排序后逐项比较：数量不同或任一对应项超容差 → 视为不等（返回 false），
    /// 并回填首个不一致的原始表达式用于提示。
    /// </summary>
    private static bool NumericSetsEqual(
        IReadOnlyList<(double BaseValue, string Dimension, string OriginalExpression)> srcList,
        IReadOnlyList<(double BaseValue, string Dimension, string OriginalExpression)> candList,
        out string srcExpr,
        out string candExpr)
    {
        var srcSorted = CollapseDuplicateValues(srcList);
        var candSorted = CollapseDuplicateValues(candList);

        if (srcSorted.Count != candSorted.Count)
        {
            srcExpr = string.Join("、", srcSorted.Select(v => v.OriginalExpression));
            candExpr = string.Join("、", candSorted.Select(v => v.OriginalExpression));
            return false;
        }

        for (var i = 0; i < srcSorted.Count; i++)
        {
            if (AreNumericConflict(srcSorted[i].BaseValue, candSorted[i].BaseValue))
            {
                srcExpr = srcSorted[i].OriginalExpression;
                candExpr = candSorted[i].OriginalExpression;
                return false;
            }
        }

        srcExpr = string.Empty;
        candExpr = string.Empty;
        return true;
    }

    /// <summary>
    /// 折叠容差内的重复归一值：升序排序后，相邻值在数值容差内视为同一值，仅保留一份
    /// （首个出现的原始表达式）。用于消除"同一数值的中英两份表达"造成的数量虚增，
    /// 例如"录像保存30天 / video saved for 30 day"或中英对照里重复的"10mm"。
    /// </summary>
    private static List<(double BaseValue, string Dimension, string OriginalExpression)> CollapseDuplicateValues(
        IReadOnlyList<(double BaseValue, string Dimension, string OriginalExpression)> values)
    {
        var sorted = values.OrderBy(v => v.BaseValue).ToList();
        var collapsed = new List<(double BaseValue, string Dimension, string OriginalExpression)>();

        foreach (var value in sorted)
        {
            if (collapsed.Count > 0 && !AreNumericConflict(collapsed[^1].BaseValue, value.BaseValue))
                continue;

            collapsed.Add(value);
        }

        return collapsed;
    }

    private static bool AreNumericConflict(double a, double b)
    {
        if (a == 0 && b == 0) return false;
        var maxAbs = Math.Max(Math.Abs(a), Math.Abs(b));
        return Math.Abs(a - b) / maxAbs > NumericCompareToleranceRatio;
    }

    // ── 尺寸元组冲突（位置敏感） ───────────────────────────────────────────

    // 尺寸元组：数字+可选单位(×|x|*|✕ 数字+可选单位)+，如 200mm×100mm / 200x100。
    // 与普通数值不同，元组的"位置"有语义（长×宽×高），不能排序后比较。
}
