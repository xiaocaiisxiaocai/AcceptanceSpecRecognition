using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public sealed partial class SemanticConflictScanner
{
    private static readonly Regex DimensionTupleRegex = new(
        @"-?\d+(?:[.,]\d+)?\s*[A-Za-z%°℃℉Ωμ一-鿿]*" +
        @"(?:\s*[×x*✕╳]\s*-?\d+(?:[.,]\d+)?\s*[A-Za-z%°℃℉Ωμ一-鿿]*)+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TupleSplitRegex = new(
        @"\s*[×x*✕╳]\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TupleDimensionPartRegex = new(
        @"^\s*(?<num>-?\d+(?:[.,]\d+)?)(?<unit>[A-Za-z%°℃℉Ωμ一-鿿]*)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 尺寸元组按位置逐项比较：200×100×50 与 100×200×50 虽数值集合相同，
    /// 但长宽高分配不同，是实质冲突。修复"排序后多重集相等"会漏判这类元组的漏洞。
    /// </summary>
    private void ScanDimensionTupleConflicts(MatchEvidence evidence, string srcText, string candText)
    {
        var srcTuples = ExtractDimensionTuples(srcText);
        var candTuples = ExtractDimensionTuples(candText);
        if (srcTuples.Count == 0 || candTuples.Count == 0)
            return;

        // 源中每个元组，若候选存在"维数相同的同类元组"却无任一逐位相等者，则判冲突。
        // 维数不同（2D vs 3D）不在此判定，交由数值/语义层处理，避免误报。
        foreach (var (srcExpr, srcDims) in srcTuples)
        {
            var comparable = candTuples.Where(c => c.Dims.Count == srcDims.Count).ToList();
            if (comparable.Count == 0)
                continue;

            if (comparable.Any(c => TuplePositionsEqual(srcDims, c.Dims)))
                continue;

            var candExpr = comparable[0].Expr;
            var msg = $"尺寸元组不一致：{srcExpr} vs {candExpr}（按长宽高位置比较，数值分配不同）";
            evidence.Issues.Add(new MatchIssue
            {
                Code = "dimension_tuple_conflict",
                Severity = "hard_conflict",
                FieldName = "尺寸元组",
                SourceValue = srcExpr,
                CandidateValue = candExpr,
                Message = msg,
                SuggestedAction = "尺寸各维数值分配不同，请人工确认"
            });
            evidence.Conflicts.Add(msg);
        }
    }

    private List<(string Expr, List<double> Dims)> ExtractDimensionTuples(string text)
    {
        var result = new List<(string, List<double>)>();
        foreach (Match match in DimensionTupleRegex.Matches(text))
        {
            var dims = TupleSplitRegex.Split(match.Value)
                .Select(ParseTupleDimensionPart)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            if (dims.Count >= 2)
                result.Add((match.Value, dims));
        }

        return result;
    }

    private double? ParseTupleDimensionPart(string part)
    {
        var match = TupleDimensionPartRegex.Match(part);
        if (!match.Success)
            return null;

        var numText = match.Groups["num"].Value.Replace(",", ".");
        if (!double.TryParse(
                numText,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value))
            return null;

        var unit = match.Groups["unit"].Value;
        if (string.IsNullOrWhiteSpace(unit))
            return value;

        return _canonicalizer.TryNormalizeToBaseUnit(value, unit, out var baseValue, out var dimension) &&
               string.Equals(dimension, "length", StringComparison.Ordinal)
            ? baseValue
            : value;
    }

    private static bool TuplePositionsEqual(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (AreNumericConflict(a[i], b[i]))
                return false;
        }

        return true;
    }

    // ── 比较符/边界方向冲突 ───────────────────────────────────────────────

    private static void ScanComparatorConflicts(MatchEvidence evidence, string srcText, string candText)
    {
        var srcOps = ExtractComparators(srcText);
        var candOps = ExtractComparators(candText);

        if (srcOps.Count == 0 || candOps.Count == 0)
            return;

        if (srcOps.SequenceEqual(candOps))
            return;

        // 只比较两边不共同出现的比较符，避免"≥下限且≤上限"这类正常范围表达
        // 因内部同时包含 ≥/≤ 被交叉比较成方向冲突。
        var srcOnlyOps = srcOps.Except(candOps).ToList();
        var candOnlyOps = candOps.Except(srcOps).ToList();
        foreach (var srcOp in srcOnlyOps)
        {
            foreach (var candOp in candOnlyOps)
            {
                if (AreComparatorConflict(srcOp, candOp))
                {
                    var msg = $"比较符方向冲突：源项 \"{srcOp}\" vs 候选 \"{candOp}\"";
                    // = 与 ≥/≤（含边界）属于语义警告，不构成硬冲突：=100 满足 ≥100 或 ≤100。
                    // = 与 >/< （不含边界）是真冲突：=100 不满足 >100 或 <100。
                    var isBoundaryInclusion =
                        (srcOp == "=" && (candOp == "≥" || candOp == "≤")) ||
                        (candOp == "=" && (srcOp == "≥" || srcOp == "≤"));
                    evidence.Issues.Add(new MatchIssue
                    {
                        Code = "comparator_conflict",
                        Severity = isBoundaryInclusion ? "warning" : "hard_conflict",
                        FieldName = "比较符",
                        SourceValue = srcOp,
                        CandidateValue = candOp,
                        Message = msg,
                        SuggestedAction = isBoundaryInclusion
                            ? "精确值与不等式边界需确认是否满足"
                            : "比较符方向不同，请人工确认"
                    });
                    if (!isBoundaryInclusion)
                        evidence.Conflicts.Add(msg);
                }
            }
        }
    }

    private static List<string> ExtractComparators(string text)
    {
        return ComparatorRegex.Matches(text)
            .Select(m => NormalizeComparator(m.Groups["op"].Value))
            .Distinct()
            .ToList();
    }

    private static string NormalizeComparator(string op)
    {
        return op switch
        {
            "不超过" or "不大于" or "小于等于" => "≤",
            "不低于" or "不小于" or "大于等于" => "≥",
            "大于" => ">",
            "小于" => "<",
            "约等于" => "≈",
            _ => op
        };
    }

    private static bool AreComparatorConflict(string a, string b)
    {
        if (a == b) return false;

        // ≥ vs ≤ / > vs < 互为冲突；> vs = / ≥ vs = 方向也冲突
        return (a == "≥" && b == "≤") || (a == "≤" && b == "≥") ||
               (a == ">" && b == "<") || (a == "<" && b == ">") ||
               (a == "≥" && b == "<") || (a == ">" && b == "≤") ||
               (a == "≤" && b == ">") || (a == "<" && b == "≥") ||
               (a == ">" && b == "=") || (a == "=" && b == ">") ||
               (a == "≥" && b == "=") || (a == "=" && b == "≥") ||
               (a == "<" && b == "=") || (a == "=" && b == "<") ||
               (a == "≤" && b == "=") || (a == "=" && b == "≤");
    }

    // ── 极性/方向反义冲突 ─────────────────────────────────────────────────

    private static void ScanPolarityConflicts(MatchEvidence evidence, string srcText, string candText)
    {
        foreach (var (termA, termB) in PolarityPairs)
        {
            var srcHasA = ContainsTerm(srcText, termA);
            var srcHasB = ContainsTerm(srcText, termB);
            var candHasA = ContainsTerm(candText, termA);
            var candHasB = ContainsTerm(candText, termB);

            // 仅当一侧"纯A极性"、另一侧"纯B极性"才判冲突。
            // 若任一侧同时出现A和B（如"正转/反转切换"），方向不明确，不下硬冲突结论，交由后续语义判断。
            var srcOnlyA = srcHasA && !srcHasB;
            var srcOnlyB = srcHasB && !srcHasA;
            var candOnlyA = candHasA && !candHasB;
            var candOnlyB = candHasB && !candHasA;

            if ((srcOnlyA && candOnlyB) || (srcOnlyB && candOnlyA))
            {
                var srcTerm = srcOnlyA ? termA : termB;
                var candTerm = candOnlyA ? termA : termB;
                var msg = $"方向/极性冲突：源项含 \"{srcTerm}\"，候选含 \"{candTerm}\"";
                evidence.Issues.Add(new MatchIssue
                {
                    Code = "polarity_conflict",
                    Severity = "hard_conflict",
                    FieldName = "方向/极性",
                    SourceValue = srcTerm,
                    CandidateValue = candTerm,
                    Message = msg,
                    SuggestedAction = "语义方向相反，请人工确认"
                });
                evidence.Conflicts.Add(msg);
            }
        }
    }

    private static bool ContainsTerm(string text, string term)
    {
        return text.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private static void ScanNegativePrefixConflicts(MatchEvidence evidence, string srcText, string candText)
    {
        var srcStatements = ExtractPolarityStatements(srcText);
        var candStatements = ExtractPolarityStatements(candText);
        if (srcStatements.Count == 0 || candStatements.Count == 0)
            return;

        foreach (var src in srcStatements)
        {
            foreach (var cand in candStatements)
            {
                if (src.IsNegative == cand.IsNegative ||
                    !string.Equals(src.Subject, cand.Subject, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var msg = $"否定前缀冲突：源项 \"{src.Text}\" vs 候选 \"{cand.Text}\"";
                evidence.Issues.Add(new MatchIssue
                {
                    Code = "negative_prefix_conflict",
                    Severity = "hard_conflict",
                    FieldName = "否定语义",
                    SourceValue = src.Text,
                    CandidateValue = cand.Text,
                    Message = msg,
                    SuggestedAction = "一侧为肯定要求、一侧为否定要求，请人工确认"
                });
                evidence.Conflicts.Add(msg);
            }
        }
    }

}
